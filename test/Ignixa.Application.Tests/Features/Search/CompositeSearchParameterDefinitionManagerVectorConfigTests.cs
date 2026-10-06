// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Search.Definition;
using Ignixa.Search.Models;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Application.Tests.Features.Search;

/// <summary>
/// Coverage for the second hop of the package vector-config path:
/// <see cref="CompositeSearchParameterDefinitionManager"/> converting an
/// <see cref="ActiveSearchParameter"/> carrying a <see cref="VectorSearchConfig"/> (or a flagged
/// invalid one) into the Core <see cref="Ignixa.Search.Models.SearchParameterInfo"/> that query and
/// indexing code consume. <see cref="PackageResourceMapperVectorConfigTests"/> only covers the first
/// hop (JSON to the conformance DTO); nothing previously exercised
/// <see cref="CompositeSearchParameterDefinitionManager"/>'s public API end to end for this field.
/// </summary>
public class CompositeSearchParameterDefinitionManagerVectorConfigTests
{
    private static readonly VectorSearchConfig ValidVectorConfig = new(
        VectorTextExtractionPolicy.Concatenate,
        MaxInputTokens: 8000,
        MinimumScore: 0.5m,
        ChunkSizeTokens: 256,
        ChunkOverlapTokens: 32);

    [Fact]
    public async Task GivenActiveSearchParameterWithVectorConfig_WhenConverted_ThenCoreInfoCarriesVectorConfigAndIsSemantic()
    {
        using var state = new ConformanceState();
        var store = Substitute.For<ISourceEventStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(Events(ValidVectorConfig));
        await state.InitializeFromEventsAsync(store, CancellationToken.None);

        var manager = BuildManager(state);
        await manager.InitializeAsync();

        manager.TryGetSearchParameter("Patient", "semantic-text", out var searchParameter).ShouldBeTrue();
        searchParameter.VectorConfig.ShouldBe(ValidVectorConfig);
        searchParameter.IsSemantic.ShouldBeTrue();
        searchParameter.IsSupported.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenActiveSearchParameterWithInvalidVectorConfig_WhenConverted_ThenCoreInfoIsUnsupported()
    {
        using var state = new ConformanceState();
        var store = Substitute.For<ISourceEventStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(Events(vectorConfig: null, hasInvalidVectorConfig: true));
        await state.InitializeFromEventsAsync(store, CancellationToken.None);

        var manager = BuildManager(state);
        await manager.InitializeAsync();

        manager.TryGetSearchParameter("Patient", "semantic-text", out var searchParameter).ShouldBeTrue();
        searchParameter.VectorConfig.ShouldBeNull();
        searchParameter.IsSemantic.ShouldBeFalse();
        searchParameter.IsSupported.ShouldBeFalse();
    }

    private static CompositeSearchParameterDefinitionManager BuildManager(ConformanceState state)
    {
        var baseManager = new SearchParameterDefinitionManager(
            FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);
        return new CompositeSearchParameterDefinitionManager(
            baseManager,
            state,
            null,
            NullLogger<CompositeSearchParameterDefinitionManager>.Instance,
            new SearchParameterResolutionOptions { EagerLoadPackageSearchParameters = true });
    }

    private static async IAsyncEnumerable<SourceEvent> Events(
        VectorSearchConfig? vectorConfig = null,
        bool hasInvalidVectorConfig = false)
    {
        await Task.CompletedTask;
        yield return new SourceEvent(
            1,
            "package-vector-config",
            nameof(SearchParameterActivated),
            new SearchParameterActivated(
                "http://example.org/SearchParameter/semantic-text",
                "semantic-text",
                "Patient",
                "Patient.name.family",
                SearchParamType.Special,
                "vector-config.package@1.0",
                Overrides: null,
                SearchParamId: 1,
                TargetResourceTypes: null,
                Components: null,
                Name: null,
                Description: null,
                VectorConfig: vectorConfig,
                HasInvalidVectorConfig: hasInvalidVectorConfig),
            DateTimeOffset.UtcNow);
    }
}
