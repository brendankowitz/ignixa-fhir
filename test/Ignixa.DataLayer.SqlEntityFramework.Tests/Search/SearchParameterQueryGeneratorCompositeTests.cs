// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests.Search;

/// <summary>
/// End-to-end coverage (parser through SearchParameterQueryGenerator) for composite structure
/// preservation: replaces ComponentIndex-heuristic extraction with direct CompositeComponentExpression
/// reads, fixes OR-of-value-groups (previously ANDed components across groups), and replaces
/// IsReferenceExpression/IsTokenExpression sniffing with effective-type-based ordering.
/// CompositeSearchParameterQueryGeneratorTests.cs (hand-built expressions, calls the composite
/// generator directly) is untouched by this change - do not add anything there.
/// </summary>
public class SearchParameterQueryGeneratorCompositeTests : TestBase
{
    private const short ObservationTypeId = 3;
    private const short CodeValueQuantityParamId = 200;
    private const short RelationshipParamId = 201;

    private readonly SearchParameterQueryGenerator _generator;
    private readonly SearchParameterExpressionParser _parser;

    public SearchParameterQueryGeneratorCompositeTests()
    {
        var compositeGenerator = new CompositeSearchParameterQueryGenerator(
            Context,
            Cache,
            LoggerFactory.CreateLogger<CompositeSearchParameterQueryGenerator>());

        _generator = new SearchParameterQueryGenerator(
            Context,
            Cache,
            LoggerFactory.CreateLogger<SearchParameterQueryGenerator>(),
            compositeGenerator);

        // The Reference|Token relationship tests below parse real reference values (e.g.
        // "DocumentReference/doc-abc") end-to-end through SearchParameterExpressionParser, which
        // requires a working IReferenceSearchValueParser - an unconfigured NSubstitute mock returns
        // null from Parse(), which SearchValueExpressionBuilderHelper.Build rejects via
        // EnsureArg.IsNotNull. Use the real parser backed by a minimal schema stub instead.
        var fhirSchema = Substitute.For<IFhirSchemaProvider>();
        fhirSchema.ResourceTypeNames.Returns(new HashSet<string> { "DocumentReference" });

        _parser = new SearchParameterExpressionParser(
            new ReferenceSearchValueParser(fhirSchema),
            Substitute.For<IFhirSchemaProvider>());
    }

    private static Uri CompositeUrl(string code) => new($"http://example.org/SearchParameter/{code}");

    private static SearchParameterInfo CreateCodeValueQuantityComposite()
    {
        var codeComponent = new SearchParameterInfo("code", "code", SearchParamType.Token);
        var quantityComponent = new SearchParameterInfo("value-quantity", "value-quantity", SearchParamType.Quantity);

        return new SearchParameterInfo(
            "code-value-quantity",
            "code-value-quantity",
            SearchParamType.Composite,
            CompositeUrl("code-value-quantity"),
            components:
            [
                new SearchParameterComponentInfo { ResolvedSearchParameter = codeComponent },
                new SearchParameterComponentInfo { ResolvedSearchParameter = quantityComponent },
            ]);
    }

    private static SearchParameterInfo CreateRelationshipComposite()
    {
        var referenceComponent = new SearchParameterInfo("relationship-target", "relationship-target", SearchParamType.Reference);
        var codeComponent = new SearchParameterInfo("relationship-type", "relationship-type", SearchParamType.Token);

        return new SearchParameterInfo(
            "relationship",
            "relationship",
            SearchParamType.Composite,
            CompositeUrl("relationship"),
            components:
            [
                new SearchParameterComponentInfo { ResolvedSearchParameter = referenceComponent },
                new SearchParameterComponentInfo { ResolvedSearchParameter = codeComponent },
            ]);
    }

    private async Task<long> CreateObservationWithTokenQuantityAsync(string resourceId, string code, decimal low, decimal high)
    {
        var resource = CreateResource(ObservationTypeId, resourceId);

        Context.TokenQuantityCompositeSearchParams.Add(new TokenQuantityCompositeSearchParamEntity
        {
            ResourceTypeId = ObservationTypeId,
            ResourceSurrogateId = resource.ResourceSurrogateId,
            SearchParamId = CodeValueQuantityParamId,
            Code1 = code,
            SystemId1 = null,
            LowValue = low,
            HighValue = high,
        });
        await Context.SaveChangesAsync();

        return resource.ResourceSurrogateId;
    }

    private async Task<long> CreateDocumentReferenceRelationshipAsync(string resourceId, string referenceResourceId, string tokenCode)
    {
        var resource = CreateResource(ObservationTypeId, resourceId);

        Context.ReferenceTokenCompositeSearchParams.Add(new ReferenceTokenCompositeSearchParamEntity
        {
            ResourceTypeId = ObservationTypeId,
            ResourceSurrogateId = resource.ResourceSurrogateId,
            SearchParamId = RelationshipParamId,
            ReferenceResourceId1 = referenceResourceId,
            Code2 = tokenCode,
            SystemId2 = null,
        });
        await Context.SaveChangesAsync();

        return resource.ResourceSurrogateId;
    }

    private async Task<List<long>> RunCompositeSearchAsync(SearchParameterInfo composite, short searchParamId, string queryValue)
    {
        Context.SearchParams.Add(new SearchParamEntity
        {
            SearchParamId = searchParamId,
            Uri = $"http://example.org/SearchParameter/{composite.Code}",
            Status = "Enabled",
            LastUpdated = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var expression = (SearchParameterExpression)_parser.Parse(composite, modifier: null, queryValue);
        var query = await _generator.GenerateQueryAsync(ObservationTypeId, expression, CancellationToken.None);

        // Some code paths (e.g. the ambiguous-order graceful-empty fallback) return a plain
        // Enumerable.Empty<long>().AsQueryable() rather than an EF-backed queryable, which doesn't
        // implement IAsyncEnumerable - EF's ToListAsync() throws for those. ToList() works uniformly
        // for both EF-backed and plain LINQ-to-objects queryables.
        return query.ToList();
    }

    [Fact]
    public async Task GivenTokenQuantityComposite_WhenSingleValueGroup_ThenReturnsMatchingResource()
    {
        var matching = await CreateObservationWithTokenQuantityAsync("obs-match", "8462-4", 80m, 80m);
        await CreateObservationWithTokenQuantityAsync("obs-nomatch", "8462-4", 90m, 90m);

        var results = await RunCompositeSearchAsync(CreateCodeValueQuantityComposite(), CodeValueQuantityParamId, "8462-4$80");

        results.ShouldBe(new[] { matching });
    }

    [Fact]
    public async Task GivenTokenQuantityComposite_WhenOrOfTwoValueGroups_ThenUnionsPerGroupResultsInsteadOfAndingAcrossGroups()
    {
        // Regression coverage for the confirmed pre-existing bug: today's ComponentIndex-based
        // extraction merges components across OR groups by index and ANDs them, so
        // "8462-4$80,8462-5$90" would incorrectly require a single row matching code=8462-4 AND
        // code=8462-5 AND value=80 AND value=90 simultaneously - impossible, always empty.
        // Correct FHIR semantics: each comma-separated group is an independent match candidate,
        // OR'd together.
        var matchesGroup1 = await CreateObservationWithTokenQuantityAsync("obs-group1", "8462-4", 80m, 80m);
        var matchesGroup2 = await CreateObservationWithTokenQuantityAsync("obs-group2", "8462-5", 90m, 90m);
        await CreateObservationWithTokenQuantityAsync("obs-neither", "8462-6", 70m, 70m);

        var results = await RunCompositeSearchAsync(CreateCodeValueQuantityComposite(), CodeValueQuantityParamId, "8462-4$80,8462-5$90");

        results.OrderBy(r => r).ShouldBe(new[] { matchesGroup1, matchesGroup2 }.OrderBy(r => r));
    }

    [Fact]
    public async Task GivenRelationshipComposite_WhenValuesMatchStaticDefinitionOrder_ThenResolvesCorrectly()
    {
        var matching = await CreateDocumentReferenceRelationshipAsync("docref-1", "doc-abc", "replaces");

        var results = await RunCompositeSearchAsync(CreateRelationshipComposite(), RelationshipParamId, "DocumentReference/doc-abc$replaces");

        results.ShouldBe(new[] { matching });
    }

    [Fact]
    public async Task GivenRelationshipComposite_WhenValuesAreSwappedRelativeToStaticDefinition_ThenStillResolvesCorrectly()
    {
        // Static definition order is [Reference, Token], but the value at position 0 is Token-shaped
        // (using the explicit "|code" form so SearchParameterExpressionParser's value-shape inference
        // actually recognizes it as a token rather than falling back to the static Reference
        // definition - a bare code with no separator is not classified as Token-shaped) and position 1
        // is Reference-shaped - the DocumentReference "relationship" swap this composite type's
        // IsReferenceExpression/IsTokenExpression sniffing existed to handle.
        // GenerateReferenceTokenGroupQueryAsync must resolve by effective type, not position.
        var matching = await CreateDocumentReferenceRelationshipAsync("docref-1", "doc-abc", "replaces");

        var results = await RunCompositeSearchAsync(CreateRelationshipComposite(), RelationshipParamId, "|replaces$DocumentReference/doc-abc");

        results.ShouldBe(new[] { matching });
    }

    [Fact]
    public async Task GivenRelationshipComposite_WhenBothValuesInferTheSameEffectiveType_ThenReturnsEmptyWithoutThrowing()
    {
        // Ambiguous order: both values look Reference-shaped. Deliberate behavior change from
        // today's "assume position order, return plausible-garbage filters" fallback to
        // "warn and return empty results" - see Global Constraints.
        await CreateDocumentReferenceRelationshipAsync("docref-1", "doc-abc", "replaces");

        var results = await RunCompositeSearchAsync(CreateRelationshipComposite(), RelationshipParamId, "DocumentReference/doc-abc$DocumentReference/doc-xyz");

        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenCompositeWithUnknownType_WhenSearched_ThenFallsBackToNonCompositeWithoutThrowing()
    {
        var singleComponent = new SearchParameterInfo(
            "single-component",
            "single-component",
            SearchParamType.Composite,
            CompositeUrl("single-component"),
            components: [new SearchParameterComponentInfo { ResolvedSearchParameter = new SearchParameterInfo("code", "code", SearchParamType.Token) }]);

        await Should.NotThrowAsync(async () => await RunCompositeSearchAsync(singleComponent, 202, "8462-4"));
    }
}
