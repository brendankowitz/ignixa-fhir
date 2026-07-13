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
/// Regression coverage for the composite paths that return before any collation-dependent comparison:
/// the ambiguous-effective-type graceful-empty fallback and the unknown-composite-type fallback.
/// Collation-dependent composite group/order coverage (OR-of-value-groups union, effective-type
/// order-swap resolution) lives in test/Ignixa.Api.E2ETests/Search/DataTypes/CompositeSearchTests.cs -
/// EF Core's InMemory provider cannot translate EF.Functions.Collate, which these read paths now use.
/// CompositeSearchParameterQueryGeneratorTests.cs (hand-built expressions, calls the composite
/// generator directly) is untouched by this change - do not add anything there.
/// </summary>
public class SearchParameterQueryGeneratorCompositeTests : TestBase
{
    private const short ObservationTypeId = 3;
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
