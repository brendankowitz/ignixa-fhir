using Ignixa.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;
using SearchComparator = Ignixa.Specification.ValueSets.Normative.SearchComparator;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;
using SortOrder = Ignixa.Search.Expressions.SortOrder;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;

/// <summary>
/// Proves the compiler-driven <see cref="Ignixa.DataLayer.SqlServer.Search.SqlServerCompiledSearchService"/>
/// agrees with the legacy EF-based <see cref="Ignixa.DataLayer.SqlEntityFramework.Search.SqlEntityFrameworkSearchService"/>
/// on sort and offset paging -- the third and last of 3 differential-search harness tasks (Task 11 covered
/// leaf/composite types, count, <c>:missing</c>; Task 12 covered chain, include/revinclude, compartment).
/// Sibling file to <see cref="CompiledSearchDifferentialTests"/> and
/// <see cref="CompiledSearchChainIncludeCompartmentDifferentialTests"/>, split out per this initiative's
/// established file-size discipline -- deliberately self-contained (its own <c>ParameterManager</c>/
/// <c>CreateResourceAsync</c>/<c>CollectAsync</c> helpers, not shared with either sibling), matching every
/// other *DifferentialTests.cs sibling in this folder.
/// </summary>
public class CompiledSearchSortPagingDifferentialTests
{
    // Pure, I/O-free lookup structure over the pre-generated R4 catalog -- see CompiledSearchDifferentialTests'
    // identical field for the full rationale.
    private static readonly SearchParameterDefinitionManager ParameterManager = new(
        FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);

    // "_id" and "_lastUpdated" are resource-column keys (no SearchParamId, no catalog seeding needed) --
    // constructed the same way EndToEndCompilationTests.cs's own real R4-parity fixtures do, rather than
    // resolved through ParameterManager (which has no entry for either, since neither is a real indexed
    // search parameter).
    private static readonly SearchParameterInfo IdParameter = new(
        "_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));

    private static readonly SearchParameterInfo LastUpdatedParameter = new(
        "_lastUpdated", "_lastUpdated", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Resource-lastUpdated"));

    private static async Task CreateResourceAsync(
        DifferentialTestHarness harness,
        string resourceType,
        string resourceId,
        IReadOnlyList<object>? searchIndices,
        CancellationToken cancellationToken)
    {
        var resource = new ResourceWrapper(
            resourceType,
            resourceId,
            "1",
            DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"{{resourceType}}","id":"{{resourceId}}"}"""),
            new ResourceRequest("PUT", $"{resourceType}/{resourceId}"))
        {
            SearchIndices = searchIndices,
        };

        await harness.LegacyRepository.CreateOrUpdateAsync(resource with { }, cancellationToken);
        await harness.NewRepository.CreateOrUpdateAsync(resource with { }, cancellationToken);
    }

    private static async Task<List<SearchEntryResult>> CollectAsync(IAsyncEnumerable<SearchEntryResult> results)
    {
        var list = new List<SearchEntryResult>();
        await foreach (var result in results)
        {
            list.Add(result);
        }

        return list;
    }

    [Fact]
    public async Task GivenADescendingSortWithNoMissingValues_WhenSearchedOnBothEngines_ThenReturnsTheSameResultsInTheSameOrder()
    {
        // Arrange -- Patient?_sort=-family, every resource has the sorted parameter present (no
        // Valued/MissingPrimary split to worry about on the compiled side, no NULL-ordering question on
        // the legacy side). Distinct, plain-ASCII values so there is no tie-break ambiguity between the
        // two engines' differing tie-break mechanisms (legacy: ThenBy(ResourceSurrogateId) always
        // ascending; compiler: m.T1 ASC, m.Sid1 ASC).
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var familyParam = ParameterManager.GetSearchParameter("Patient", "family");
        await harness.SeedSearchParameterCatalogAsync([familyParam.Url!], CancellationToken.None);

        var values = new[] { "Apple", "Banana", "Cherry", "Date" };
        var ids = new List<string>();
        foreach (var value in values)
        {
            var id = $"diff-sort-desc-{Guid.NewGuid():N}";
            ids.Add(id);
            await CreateResourceAsync(harness, "Patient", id,
                [new SearchIndexEntry(familyParam, new StringSearchValue(value) { IsMin = true, IsMax = true })],
                CancellationToken.None);
        }

        var options = new SearchOptions
        {
            ResourceType = "Patient",
            Sort = [new SortExpression(familyParam, SortOrder.Descending)],
        };

        // Act
        var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
        var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert -- exact sequence equality (order matters, unlike Tasks 11-12's set-based comparisons).
        // Descending family: Date, Cherry, Banana, Apple.
        legacyResults.Select(r => r.ResourceId).ShouldBe(newResults.Select(r => r.ResourceId));
        legacyResults.Select(r => r.ResourceId).ShouldBe([ids[3], ids[2], ids[1], ids[0]]);
    }

    [Fact]
    public async Task GivenAnAscendingSortWithSomeResourcesMissingTheSortKey_WhenSearchedOnBothEngines_ThenTheyDeliberatelyDivergeInOrder()
    {
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var familyParam = ParameterManager.GetSearchParameter("Patient", "family");
        await harness.SeedSearchParameterCatalogAsync([familyParam.Url!], CancellationToken.None);

        // Arrange -- 2 resources WITH the sort parameter set, 2 WITHOUT, ascending sort.
        var withId1 = $"diff-sort-missing-with1-{Guid.NewGuid():N}";
        var withId2 = $"diff-sort-missing-with2-{Guid.NewGuid():N}";
        var withoutId1 = $"diff-sort-missing-without1-{Guid.NewGuid():N}";
        var withoutId2 = $"diff-sort-missing-without2-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Patient", withId1,
            [new SearchIndexEntry(familyParam, new StringSearchValue("Alpha") { IsMin = true, IsMax = true })], CancellationToken.None);
        await CreateResourceAsync(harness, "Patient", withId2,
            [new SearchIndexEntry(familyParam, new StringSearchValue("Beta") { IsMin = true, IsMax = true })], CancellationToken.None);
        await CreateResourceAsync(harness, "Patient", withoutId1, null, CancellationToken.None);
        await CreateResourceAsync(harness, "Patient", withoutId2, null, CancellationToken.None);

        var options = new SearchOptions
        {
            ResourceType = "Patient",
            Sort = [new SortExpression(familyParam, SortOrder.Ascending)],
        };

        // Act
        var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
        var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert -- documented divergence: legacy sorts NULL/missing keys FIRST in ascending (SQL Server
        // default; ApplySort's MIN() subquery over StringSearchParam evaluates to NULL for a resource
        // with no row, and SQL Server orders NULL before any non-NULL value ascending), the compiler's
        // two-phase model always places missing-value rows LAST regardless of direction (Valued phase
        // exhausted before MissingPrimary is ever touched). Same 4 resources on both sides (set-equal),
        // but the FIRST result differs: legacy's first result has a missing sort key, the compiler's
        // first result has a valued one.
        legacyResults.Select(r => r.ResourceId).OrderBy(x => x).ShouldBe(newResults.Select(r => r.ResourceId).OrderBy(x => x));
        legacyResults[0].ResourceId.ShouldNotBe(newResults[0].ResourceId);
        new[] { withoutId1, withoutId2 }.ShouldContain(legacyResults[0].ResourceId);
        new[] { withId1, withId2 }.ShouldContain(newResults[0].ResourceId);
    }

    [Fact]
    public async Task GivenAnIdSort_WhenSearchedOnBothEngines_ThenReturnsTheSameResultsInTheSameOrder()
    {
        // Arrange -- Patient?_sort=_id (and its descending mirror). Now safe to exercise differentially
        // per Task 6's fix: legacy orders by the native ResourceId string column (ApplySort's "_id"
        // case), the compiler's SortKeyKind.ResourceId join orders by the SAME underlying
        // dbo.Resource.ResourceId column via a different mechanism -- both should agree exactly on
        // order, since both are ordering the same physical string column, with no NULLs possible (every
        // resource always has a ResourceId).
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);

        var ids = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var id = $"diff-idsort-{Guid.NewGuid():N}";
            ids.Add(id);
            await CreateResourceAsync(harness, "Patient", id, null, CancellationToken.None);
        }

        var ascendingOptions = new SearchOptions { ResourceType = "Patient", Sort = [new SortExpression(IdParameter, SortOrder.Ascending)] };
        var descendingOptions = new SearchOptions { ResourceType = "Patient", Sort = [new SortExpression(IdParameter, SortOrder.Descending)] };

        // Act
        var legacyAscending = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(ascendingOptions, CancellationToken.None));
        var newAscending = await CollectAsync(harness.NewSearchService.SearchStreamAsync(ascendingOptions, CancellationToken.None));
        var legacyDescending = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(descendingOptions, CancellationToken.None));
        var newDescending = await CollectAsync(harness.NewSearchService.SearchStreamAsync(descendingOptions, CancellationToken.None));

        // Assert -- exact sequence equality (order matters) between the two engines, in both directions.
        legacyAscending.Select(r => r.ResourceId).ShouldBe(newAscending.Select(r => r.ResourceId));
        legacyDescending.Select(r => r.ResourceId).ShouldBe(newDescending.Select(r => r.ResourceId));

        // Sanity: sorting is genuinely happening (not coincidentally identical regardless of direction),
        // and every id is present exactly once -- proven without any assumption about SQL Server's
        // collation matching a specific .NET StringComparer.
        legacyAscending.Select(r => r.ResourceId).Reverse().ShouldBe(legacyDescending.Select(r => r.ResourceId));
        legacyAscending.Select(r => r.ResourceId).OrderBy(x => x).ShouldBe(ids.OrderBy(x => x));
    }

    [Fact]
    public async Task GivenOffsetPagingAcrossAPageBoundary_WhenSearchedOnBothEngines_ThenPage2MatchesAndTheTwoPagesTogetherCoverAllResourcesWithNoDuplicatesOrGaps()
    {
        // Arrange -- 6 Patients, _sort=_id ascending, page size 3 (exactly 2 full pages). Mirrors the
        // real Application-layer handler's own pagination convention (SearchResourcesHandler.HandleAsync
        // requests MaxItemCount+1 to detect hasMore; StreamingBundleSerializer.SerializeWithPaginationAsync
        // renders only the first pageSize entries and, if hasMore, encodes the NEXT continuation token as
        // ContinuationToken.Encode(currentOffset + pageSize, pageSize) -- the ORIGINAL page size, not the
        // +1'd one). Task 10's straddling-page test proved only the new engine's own internal correctness
        // against itself; this is the first genuine cross-engine differential proof of this exact paging
        // shape (decode/re-encode a continuation token for page 2, using the real ContinuationToken API).
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);

        const int pageSize = 3;
        var ids = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            var id = $"diff-page-{Guid.NewGuid():N}";
            ids.Add(id);
            await CreateResourceAsync(harness, "Patient", id, null, CancellationToken.None);
        }

        var page1Options = new SearchOptions
        {
            ResourceType = "Patient",
            Sort = [new SortExpression(IdParameter, SortOrder.Ascending)],
            MaxItemCount = pageSize + 1,
        };

        // Act -- page 1 (raw stream includes the +1-for-hasMore extra row).
        var legacyPage1Raw = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(page1Options, CancellationToken.None));
        var newPage1Raw = await CollectAsync(harness.NewSearchService.SearchStreamAsync(page1Options, CancellationToken.None));

        var legacyPage1Rendered = legacyPage1Raw.Take(pageSize).Select(r => r.ResourceId).ToList();
        var newPage1Rendered = newPage1Raw.Take(pageSize).Select(r => r.ResourceId).ToList();

        // Assert -- page 1 identical on both engines, same order, same hasMore signal (raw count > pageSize).
        legacyPage1Rendered.ShouldBe(newPage1Rendered);
        (legacyPage1Raw.Count > pageSize).ShouldBeTrue();
        (newPage1Raw.Count > pageSize).ShouldBeTrue();

        var page2Token = ContinuationToken.Encode(offset: pageSize, count: pageSize);
        var page2Options = new SearchOptions
        {
            ResourceType = "Patient",
            Sort = [new SortExpression(IdParameter, SortOrder.Ascending)],
            MaxItemCount = pageSize + 1,
            ContinuationToken = page2Token,
        };

        // Act -- page 2, decoded/re-encoded via the real ContinuationToken API exactly as the
        // Application-layer handler would.
        var legacyPage2Raw = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(page2Options, CancellationToken.None));
        var newPage2Raw = await CollectAsync(harness.NewSearchService.SearchStreamAsync(page2Options, CancellationToken.None));

        var legacyPage2Rendered = legacyPage2Raw.Take(pageSize).Select(r => r.ResourceId).ToList();
        var newPage2Rendered = newPage2Raw.Take(pageSize).Select(r => r.ResourceId).ToList();

        // Assert -- page 2 identical on both engines, same order.
        legacyPage2Rendered.ShouldBe(newPage2Rendered);
        legacyPage2Rendered.Count.ShouldBe(pageSize);

        // Assert -- page 1 + page 2 together cover every one of the 6 resources exactly once: no
        // duplicates, no gaps, straddling the boundary cleanly.
        var combined = legacyPage1Rendered.Concat(legacyPage2Rendered).ToList();
        combined.Count.ShouldBe(6);
        combined.Distinct().Count().ShouldBe(6);
        combined.OrderBy(x => x, StringComparer.Ordinal).ShouldBe(ids.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task GivenAPartialPrecisionLastUpdatedSearch_WhenSearchedOnBothEngines_ThenLegacySucceedsAndCompiledThrowsRequestNotValidException()
    {
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);

        var patientId = $"diff-lastupdated-partial-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Patient", patientId, null, CancellationToken.None);

        // Arrange -- a _lastUpdated=2026 (year-only) search, which flattens to a single instant on the
        // legacy path but has Start != End on the compiler's typed IR.
        var options = new SearchOptions
        {
            ResourceType = "Patient",
            Expression = new SearchParameterExpression(
                LastUpdatedParameter,
                new SearchParameterPredicateExpression(LastUpdatedParameter, SearchComparator.Eq, modifier: null, DateTimeSearchValue.Parse("2026"))),
        };

        // Act
        var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert -- documented divergence: legacy silently flattens and searches only that single instant
        // (returns SOME result, possibly wrong/incomplete but doesn't throw); the compiler throws
        // RequestNotValidException naming ResourceColumnLoweringRule's specific message.
        legacyResults.ShouldNotBeNull();

        var ex = await Should.ThrowAsync<RequestNotValidException>(async () =>
        {
            await foreach (var _ in harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None))
            {
            }
        });
        ex.Message.ShouldContain("_lastUpdated only supports an exact instant");
    }
}
