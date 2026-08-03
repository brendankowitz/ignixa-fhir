using System.Globalization;
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
    public async Task GivenAPartialPrecisionLastUpdatedSearch_WhenSearchedOnBothEngines_ThenTheCompiledEngineMatchesTheWholeYear()
    {
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);

        var patientId = $"diff-lastupdated-partial-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Patient", patientId, null, CancellationToken.None);

        // Arrange -- a year-only _lastUpdated search over the year the resource was just written in. The
        // value has Start != End on the compiler's typed IR; the legacy path flattens it to its single
        // start instant.
        var currentYear = DateTimeOffset.UtcNow.Year.ToString(CultureInfo.InvariantCulture);
        var options = new SearchOptions
        {
            ResourceType = "Patient",
            Expression = new SearchParameterExpression(
                LastUpdatedParameter,
                new SearchParameterPredicateExpression(LastUpdatedParameter, SearchComparator.Eq, modifier: null, DateTimeSearchValue.Parse(currentYear))),
        };

        // Act
        var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
        var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert -- the divergence this test was written to document is CLOSED. It used to refuse a
        // partial-precision range outright (RequestNotValidException out of ResourceColumnLoweringRule,
        // while legacy returned something); the compiler now lowers [Start, End] as a real closed range
        // over the surrogate-id bucket, so a year-precision _lastUpdated matches a resource written
        // anywhere in that year -- which is both the FHIR semantics and what legacy already did. The two
        // engines now agree, so this asserts agreement rather than the old documented difference.
        newResults.Select(r => r.ResourceId).ShouldContain(patientId);
        legacyResults.Select(r => r.ResourceId).OrderBy(x => x, StringComparer.Ordinal)
            .ShouldBe(newResults.Select(r => r.ResourceId).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task GivenASortedIncludeSearchStraddlingThePhaseBoundary_WhenSearchedOnTheNewEngine_ThenNoDuplicateOrMislabeledEntriesAppear()
    {
        // Arrange -- final-review fix: Patient?_sort=family&_include=Patient:link, 4 Patients WITH "family"
        // set (Valued) followed by 3 WITHOUT it (MissingPrimary), where the FIRST MissingPrimary match
        // (missing00) has a Patient:link reference pointing at the LAST Valued match (valued02) that this
        // exact page will also return as a genuine Match in its own right. A page requested at offset=2,
        // count=3 (real requestedCount, after the +1-for-hasMore convention, is 4) straddles the
        // Valued/MissingPrimary boundary: Valued returns only its tail (valued02, valued03 -- 2 rows),
        // short of requestedCount, so MissingPrimary runs too, at its own offset 0, limit 4-2=2 (missing00,
        // missing01). MissingPrimary's own include stage is seeded ONLY from ITS OWN match page
        // {missing00, missing01} -- it has no visibility into Valued's match page, so its anti-join does
        // not exclude valued02, and it independently re-discovers valued02 as an Include row. Before the
        // fix, SearchStreamWithPhaseHandlingAsync concatenated both phases' raw streams with no cross-phase
        // dedup: valued02 came back TWICE (once correctly as Match from the Valued phase, once wrongly as
        // Include from the MissingPrimary phase) -- a duplicate bundle entry, one of the two copies
        // mislabeled. This test proves the fix: exactly 4 distinct (ResourceType, ResourceId) pairs, and
        // valued02's single surviving entry is Match, never demoted to Include.
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var familyParam = ParameterManager.GetSearchParameter("Patient", "family");
        var linkParam = ParameterManager.GetSearchParameter("Patient", "link");
        await harness.SeedSearchParameterCatalogAsync([familyParam.Url!, linkParam.Url!], CancellationToken.None);

        var tag = Guid.NewGuid().ToString("N");
        var valuedIds = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var id = $"diff-straddle-valued-{tag}-{i}";
            valuedIds.Add(id);
            await CreateResourceAsync(harness, "Patient", id,
                [new SearchIndexEntry(familyParam, new StringSearchValue($"family-{i:D2}") { IsMin = true, IsMax = true })],
                CancellationToken.None);
        }

        var linkTargetId = valuedIds[2]; // the Valued match the straddling page's own tail will also return.
        var valuedTailOtherId = valuedIds[3]; // the other Valued-tail match, uninvolved in the collision.

        var missingLinkedId = $"diff-straddle-missing-linked-{tag}";
        var missingPlainId = $"diff-straddle-missing-plain-{tag}";
        var missingExtraId = $"diff-straddle-missing-extra-{tag}";
        await CreateResourceAsync(harness, "Patient", missingLinkedId,
            [new SearchIndexEntry(linkParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: linkTargetId))],
            CancellationToken.None);
        await CreateResourceAsync(harness, "Patient", missingPlainId, null, CancellationToken.None);
        await CreateResourceAsync(harness, "Patient", missingExtraId, null, CancellationToken.None);

        var linkInclude = new IncludeExpression(["Patient"], linkParam, "Patient", "Patient", null, wildCard: false, reversed: false, iterate: false);
        var options = new SearchOptions
        {
            ResourceType = "Patient",
            Sort = [new SortExpression(familyParam, SortOrder.Ascending)],
            Include = [linkInclude],
            MaxItemCount = 3,
            ContinuationToken = ContinuationToken.Encode(offset: 2, count: 3),
        };

        // Act
        var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert -- no duplicate (ResourceType, ResourceId) pairs: exactly the 2 Valued-tail matches plus
        // the 2 MissingPrimary-head matches, valued02 counted ONCE despite both phases independently
        // producing a row for it.
        var identities = newResults.Select(r => (r.ResourceType, r.ResourceId)).ToList();
        identities.Distinct().Count().ShouldBe(identities.Count, "the new engine returned a duplicate (ResourceType, ResourceId) entry for a sorted, includes-bearing search that straddles the phase boundary.");

        var expectedIds = new[] { linkTargetId, valuedTailOtherId, missingLinkedId, missingPlainId };
        newResults.Select(r => r.ResourceId).OrderBy(x => x, StringComparer.Ordinal)
            .ShouldBe(expectedIds.OrderBy(x => x, StringComparer.Ordinal));

        // Assert -- valued02 (linkTargetId) is a genuine primary Match (it satisfied the Valued phase's
        // own page), and must never be demoted to Include just because MissingPrimary's independent
        // include stage also reached it through missing00's link reference. Also confirms it appears
        // exactly once (the Count assertion is redundant with the Distinct check above for THIS identity
        // specifically, kept because it is the one identity the whole test exists to protect).
        newResults.Count(r => r.ResourceId == linkTargetId).ShouldBe(1);
        newResults.Single(r => r.ResourceId == linkTargetId).SearchMode.ShouldBe(SearchEntryMode.Match);

        newResults.Single(r => r.ResourceId == valuedTailOtherId).SearchMode.ShouldBe(SearchEntryMode.Match);
        newResults.Single(r => r.ResourceId == missingLinkedId).SearchMode.ShouldBe(SearchEntryMode.Match);
        newResults.Single(r => r.ResourceId == missingPlainId).SearchMode.ShouldBe(SearchEntryMode.Match);

        // Sanity against the legacy engine: even without the compiler's two-phase split, valued02 must
        // still resolve as a genuine Match (never an Include) on a full, unpaged run of the same query --
        // confirming this test's fixture models a real FHIR search shape, not an artifact of the compiler's
        // own phase mechanics.
        var legacyFullOptions = new SearchOptions
        {
            ResourceType = "Patient",
            Sort = [new SortExpression(familyParam, SortOrder.Ascending)],
            Include = [linkInclude],
            MaxItemCount = 100,
        };
        var legacyFullResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(legacyFullOptions, CancellationToken.None));
        legacyFullResults.Single(r => r.ResourceId == linkTargetId).SearchMode.ShouldBe(SearchEntryMode.Match);
    }

    /// <summary>
    /// Simulates <c>StreamingBundleSerializer.SerializeWithPaginationAsync</c>'s own Match-counting trim
    /// logic (lines 173-217 of that file): the first <paramref name="pageSize"/> Match-mode entries in
    /// stream order are rendered, every Include-mode entry always passes through (no
    /// <c>IncludesMaxItemCount</c> configured on this test's <see cref="SearchOptions"/>), and any Match
    /// beyond <paramref name="pageSize"/> is dropped and flips <c>HasMore</c> -- the exact +1-for-hasMore
    /// sentinel mechanism the merge-order bug corrupts.
    /// </summary>
    private static (List<SearchEntryResult> Rendered, bool HasMore) ApplyPaginationTrim(
        IReadOnlyList<SearchEntryResult> raw, int pageSize)
    {
        var rendered = new List<SearchEntryResult>();
        var matchCount = 0;
        var hasMore = false;

        foreach (var entry in raw)
        {
            if (entry.SearchMode == SearchEntryMode.Match)
            {
                if (matchCount >= pageSize)
                {
                    hasMore = true;
                    continue;
                }

                matchCount++;
            }

            rendered.Add(entry);
        }

        return (rendered, hasMore);
    }

    [Fact]
    public async Task GivenAStraddlingIncludeThatAlsoLandsAtTheMissingPrimarySentinelPosition_WhenPagedAcrossTwoRealPages_ThenNoDuplicateOrDroppedEntriesAppear()
    {
        // Arrange -- final-review re-review fix: Patient?_sort=family&_include=Patient:link, 4 Patients
        // WITH "family" set (Valued: valued00..valued03) followed by 3 WITHOUT it (MissingPrimary:
        // missing00..missing02), where the LAST Valued match in this page's own window (valued03) has a
        // Patient:link reference pointing at missing01 -- the resource that ALSO happens to be, purely by
        // MissingPrimary's own internal (Sid1) tie-break order, the +1-for-hasMore SENTINEL row of the
        // MissingPrimary fetch window. This is exactly the collision the prior promotion-in-place fix got
        // wrong: it promotes missing01 to Match AT THE POSITION Valued's own include stage happened to
        // emit it (right after valued02/valued03), not at its true late (sentinel) position among
        // MissingPrimary's own matches (after missing00).
        //
        // offset=2, pageSize=3 (requestedCount=4 after the +1-for-hasMore convention): Valued returns only
        // its tail {valued02, valued03} (2 rows, short of 4), so MissingPrimary runs too, at offset 0,
        // limit 4-2=2, returning {missing00, missing01}. Valued's OWN include stage (seeded only from
        // {valued02, valued03}) independently re-discovers missing01 via valued03's link -- the same
        // cross-phase collision Task 10's straddling test already proves is de-duplicated, but this time
        // the colliding identity is ALSO the sentinel: with the buggy promote-in-place merge, the stream's
        // Match order becomes [valued02, valued03, missing01, missing00] (missing01 wrongly BEFORE
        // missing00), so the serializer's first-3-Matches page-1 render is [valued02, valued03, missing01]
        // -- missing01 wrongly SHOWN, missing00 wrongly TRIMMED as the sentinel and never re-fetched (page
        // 2's offset arithmetic, computed from the wrong trim count, starts past it). Page 2 (offset 5)
        // then independently re-fetches missing01 as a genuine MissingPrimary match, producing missing01
        // TWICE across the two pages while missing00 is silently gone from both. This test proves the
        // two-pass merge fix: missing01 appears exactly once (as Match, wherever the true global order
        // places it), and missing00 is never dropped.
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var familyParam = ParameterManager.GetSearchParameter("Patient", "family");
        var linkParam = ParameterManager.GetSearchParameter("Patient", "link");
        await harness.SeedSearchParameterCatalogAsync([familyParam.Url!, linkParam.Url!], CancellationToken.None);

        var tag = Guid.NewGuid().ToString("N");
        var valuedIds = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            valuedIds.Add($"diff-sentinel-valued-{tag}-{i}");
        }

        var missingIds = new List<string>
        {
            $"diff-sentinel-missing-{tag}-0",
            $"diff-sentinel-missing-{tag}-1",
            $"diff-sentinel-missing-{tag}-2",
        };

        // valuedIds[3] is the ONLY Valued resource carrying a link -- it points at missingIds[1], the
        // resource this whole test exists to mis-position. Created before the "missing" group so its
        // reference target id is known ahead of time; creation order within THIS loop is irrelevant to the
        // Valued phase (which orders by the explicit "family" value, not creation order).
        for (var i = 0; i < 4; i++)
        {
            var searchIndices = new List<object>
            {
                new SearchIndexEntry(familyParam, new StringSearchValue($"family-{i:D2}") { IsMin = true, IsMax = true }),
            };

            if (i == 3)
            {
                searchIndices.Add(new SearchIndexEntry(
                    linkParam,
                    new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: missingIds[1])));
            }

            await CreateResourceAsync(harness, "Patient", valuedIds[i], searchIndices, CancellationToken.None);
        }

        // Creation order here IS load-bearing: MissingPrimary's own tie-break is Sid1 (ResourceSurrogateId)
        // ascending (SqlBuilder.cs's ORDER BY ... T1 ASC, Sid1 ASC), and surrogate ids are assigned
        // monotonically in creation order -- so missingIds[0] genuinely sorts before missingIds[1] before
        // missingIds[2] within the MissingPrimary phase, exactly as this test's arithmetic requires.
        foreach (var missingId in missingIds)
        {
            await CreateResourceAsync(harness, "Patient", missingId, null, CancellationToken.None);
        }

        var sentinelId = missingIds[1];
        var droppedVictimId = missingIds[0];

        const int pageSize = 3;
        var linkInclude = new IncludeExpression(["Patient"], linkParam, "Patient", "Patient", null, wildCard: false, reversed: false, iterate: false);

        var page1Options = new SearchOptions
        {
            ResourceType = "Patient",
            Sort = [new SortExpression(familyParam, SortOrder.Ascending)],
            Include = [linkInclude],
            MaxItemCount = pageSize + 1,
            ContinuationToken = ContinuationToken.Encode(offset: 2, count: pageSize),
        };

        // Act -- page 1: a real search call at the exact offset that straddles the phase boundary.
        var page1Raw = await CollectAsync(harness.NewSearchService.SearchStreamAsync(page1Options, CancellationToken.None));
        var (page1Rendered, page1HasMore) = ApplyPaginationTrim(page1Raw, pageSize);

        // Assert -- page 1 must signal hasMore (there IS a 5th match beyond this page), proving the test
        // fixture genuinely lands at the sentinel boundary rather than exhausting the result set early.
        page1HasMore.ShouldBeTrue("page 1 should have a 5th match beyond it (the sentinel row) -- if not, this fixture is not exercising the phase-boundary/sentinel collision at all.");

        // Act -- page 2: decoded/re-encoded via the real ContinuationToken API exactly as the
        // Application-layer handler and StreamingBundleSerializer would (currentOffset + pageSize).
        var page2Token = ContinuationToken.Encode(offset: 2 + pageSize, count: pageSize);
        var page2Options = new SearchOptions
        {
            ResourceType = "Patient",
            Sort = [new SortExpression(familyParam, SortOrder.Ascending)],
            Include = [linkInclude],
            MaxItemCount = pageSize + 1,
            ContinuationToken = page2Token,
        };

        var page2Raw = await CollectAsync(harness.NewSearchService.SearchStreamAsync(page2Options, CancellationToken.None));
        var (page2Rendered, _) = ApplyPaginationTrim(page2Raw, pageSize);

        var combinedRendered = page1Rendered.Concat(page2Rendered).ToList();

        // Assert -- every expected resource (2 Valued-tail matches + all 3 MissingPrimary matches) appears
        // across the two rendered pages EXACTLY ONCE, and nothing unexpected leaked through -- this single
        // set-equality-with-multiplicity check simultaneously proves "no duplicate" and "no silent drop".
        var expectedIds = new[] { valuedIds[2], valuedIds[3], missingIds[0], missingIds[1], missingIds[2] };
        combinedRendered.Select(r => r.ResourceId).OrderBy(x => x, StringComparer.Ordinal)
            .ShouldBe(expectedIds.OrderBy(x => x, StringComparer.Ordinal));

        // Assert -- the sentinel/include-colliding resource (missingIds[1]) is present exactly once, on
        // whichever page it actually lands on, and is labeled Match (never left as, or demoted to, Include
        // just because Valued's own include stage also independently reached it).
        var sentinelOccurrences = combinedRendered.Where(r => r.ResourceId == sentinelId).ToList();
        sentinelOccurrences.Count.ShouldBe(1, "the sentinel-colliding resource was duplicated across the two pages.");
        sentinelOccurrences[0].SearchMode.ShouldBe(SearchEntryMode.Match);

        // Assert -- the true sentinel victim (missingIds[0], which the buggy in-place promotion silently
        // drops because the wrong row gets trimmed) is present exactly once and labeled Match.
        var droppedVictimOccurrences = combinedRendered.Where(r => r.ResourceId == droppedVictimId).ToList();
        droppedVictimOccurrences.Count.ShouldBe(1, "a resource was silently dropped across the two pages -- the true sentinel row never got re-fetched on page 2.");
        droppedVictimOccurrences[0].SearchMode.ShouldBe(SearchEntryMode.Match);
    }
}
