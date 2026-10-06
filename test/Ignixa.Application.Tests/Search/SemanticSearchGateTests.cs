// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Abstractions;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.Application.Features.Metadata.Segments;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Specification.Extensions;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Application.Tests.Search;

/// <summary>
/// Review Focus #5 (slice-1 plan): when <see cref="SearchParameterResolutionOptions.VectorSearchEnabled"/>
/// is false, a semantic search parameter must behave exactly as unknown everywhere -- lenient search
/// ignores it with an issue, strict search rejects it (the parser-level half of that; the HTTP 400 is
/// Task 9's E2E concern), and the CapabilityStatement omits it entirely. Enabled, the same parameter
/// parses to a <see cref="VectorSearchExpression"/> and is declared.
/// </summary>
public class SemanticSearchGateTests
{
    private static readonly VectorSearchConfig ValidVectorConfig = new(
        VectorTextExtractionPolicy.Concatenate,
        MaxInputTokens: 8000,
        MinimumScore: 0.5m,
        ChunkSizeTokens: null,
        ChunkOverlapTokens: null);

    [Fact]
    public async Task GivenFeatureEnabled_WhenResolved_ThenSemanticParameterIsSupportedAndSemantic()
    {
        var manager = await BuildManagerAsync(vectorSearchEnabled: true);

        manager.TryGetSearchParameter("Patient", "semantic-text", out var parameter).ShouldBeTrue();
        parameter.IsSemantic.ShouldBeTrue();
        parameter.IsSupported.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenFeatureDisabled_WhenResolved_ThenSemanticParameterIsUnsupportedButStillSemantic()
    {
        var manager = await BuildManagerAsync(vectorSearchEnabled: false);

        // Still resolves (ConformanceState's record of the IG package that defined it is untouched --
        // "Unsupported-invisible is fine; do not delete them from conformance state") but is flagged
        // unsupported, which is what every consumer below keys off of.
        manager.TryGetSearchParameter("Patient", "semantic-text", out var parameter).ShouldBeTrue();
        parameter.IsSemantic.ShouldBeTrue();
        parameter.IsSupported.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenFeatureDisabled_WhenLenientSearch_ThenParameterIgnoredWithIssue()
    {
        var (manager, builder) = await BuildSearchFixtureAsync(vectorSearchEnabled: false);

        SearchOptions options = builder.Build(
            "Patient",
            [new QueryParameter("semantic-text", "chest pain")]);

        options.UnsupportedParams.ShouldContain("semantic-text");
        options.UnsupportedModifierParams.ShouldNotContain("semantic-text");
        options.BundleIssues.ShouldContain(issue => issue.Diagnostics.Contains("semantic-text", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GivenFeatureDisabled_WhenStrictSearch_ThenParameterOutcomeIsUnknown()
    {
        // The HTTP 400 for handling=strict is decided at the API layer from SearchOptions.UnsupportedParams
        // (see UnsupportedModifierHandlingTests for the existing E2E precedent); this pins the parser-level
        // signal that drives it -- the parameter does not silently compile into a VectorSearchExpression.
        var (manager, builder) = await BuildSearchFixtureAsync(vectorSearchEnabled: false);

        SearchOptions options = builder.Build(
            "Patient",
            [new QueryParameter("semantic-text", "chest pain")]);

        options.UnsupportedParams.ShouldContain("semantic-text");
    }

    [Fact]
    public async Task GivenFeatureEnabled_WhenSearched_ThenParameterCompilesToVectorSearchExpression()
    {
        var (manager, builder) = await BuildSearchFixtureAsync(vectorSearchEnabled: true);

        SearchOptions options = builder.Build(
            "Patient",
            [new QueryParameter("semantic-text", "chest pain")]);

        options.UnsupportedParams.ShouldNotContain("semantic-text");
        options.Expression.ShouldBeAssignableTo<Ignixa.Search.Expressions.VectorSearchExpression>();
    }

    [Fact]
    public async Task GivenFeatureDisabled_WhenCapabilityStatementBuilt_ThenSemanticParamAbsent()
    {
        var manager = await BuildManagerAsync(vectorSearchEnabled: false);
        var searchParams = await ApplyCapabilitySegmentAsync(manager);

        searchParams.ShouldNotContain(sp => sp.Name == "semantic-text");
    }

    [Fact]
    public async Task GivenFeatureEnabled_WhenCapabilityStatementBuilt_ThenSemanticParamPresent()
    {
        var manager = await BuildManagerAsync(vectorSearchEnabled: true);
        var searchParams = await ApplyCapabilitySegmentAsync(manager);

        searchParams.ShouldContain(sp => sp.Name == "semantic-text");
    }

    private static async Task<IReadOnlyList<SearchParamJsonNode>> ApplyCapabilitySegmentAsync(
        CompositeSearchParameterDefinitionManager manager)
    {
        var versionContext = Substitute.For<IFhirVersionContext>();
        versionContext.GetSearchParameterDefinitionManager(Arg.Any<FhirVersion>(), Arg.Any<int?>()).Returns(manager);

        var segment = new SearchParameterCapabilitySegment(versionContext, NullLogger<SearchParameterCapabilitySegment>.Instance);

        var statement = new CapabilityStatementJsonNode();
        statement.Rest.Add(new RestComponentJsonNode { Mode = RestComponentJsonNode.RestfulCapabilityMode.Server });
        statement.Rest[0].Resource.Add(new ResourceComponentJsonNode { Type = "Patient" });

        await segment.ApplyAsync(statement, new CapabilityContext(FhirVersion.R4), CancellationToken.None);

        return statement.Rest[0].Resource.Single(r => r.Type == "Patient").SearchParam.ToList();
    }

    private static async Task<(CompositeSearchParameterDefinitionManager Manager, SearchOptionsBuilder Builder)> BuildSearchFixtureAsync(
        bool vectorSearchEnabled)
    {
        var manager = await BuildManagerAsync(vectorSearchEnabled);
        var schemaProvider = new R4CoreSchemaProvider();
        var referenceParser = new ReferenceSearchValueParser(schemaProvider, NullFhirBaseUriProvider.Instance);
        var valueParser = new SearchParameterExpressionParser(referenceParser, schemaProvider);
        var expressionParser = new ExpressionParser(() => manager, valueParser, schemaProvider);
        var builder = new SearchOptionsBuilder(expressionParser, manager);

        return (manager, builder);
    }

    private static async Task<CompositeSearchParameterDefinitionManager> BuildManagerAsync(bool vectorSearchEnabled)
    {
        using var state = new ConformanceState();
        var store = Substitute.For<ISourceEventStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(Events());
        await state.InitializeFromEventsAsync(store, CancellationToken.None);

        var baseManager = new SearchParameterDefinitionManager(
            FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);
        var manager = new CompositeSearchParameterDefinitionManager(
            baseManager,
            state,
            null,
            NullLogger<CompositeSearchParameterDefinitionManager>.Instance,
            new SearchParameterResolutionOptions { EagerLoadPackageSearchParameters = true, VectorSearchEnabled = vectorSearchEnabled });

        await manager.InitializeAsync();
        return manager;
    }

    private static async IAsyncEnumerable<SourceEvent> Events()
    {
        await Task.CompletedTask;
        yield return new SourceEvent(
            1,
            "semantic-search-gate",
            nameof(SearchParameterActivated),
            new SearchParameterActivated(
                "http://example.org/SearchParameter/semantic-text",
                "semantic-text",
                "Patient",
                "Patient.name.family",
                SearchParamType.Special,
                "semantic-search-gate.package@1.0",
                Overrides: null,
                SearchParamId: 1,
                TargetResourceTypes: null,
                Components: null,
                Name: null,
                Description: null,
                VectorConfig: ValidVectorConfig,
                HasInvalidVectorConfig: false),
            DateTimeOffset.UtcNow);
    }
}
