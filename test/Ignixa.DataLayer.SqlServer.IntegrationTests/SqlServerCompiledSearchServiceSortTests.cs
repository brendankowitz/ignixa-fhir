using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.DataLayer.SqlServer.Search;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using SearchIndexEntry = Ignixa.Search.Indexing.SearchIndexEntry;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Shouldly;
using Xunit;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

/// <summary>
/// Proves Task 10's two-phase Valued/MissingPrimary sort executor loop against a real SQL Server
/// instance: a page whose offset straddles the Valued/MissingPrimary boundary, and a page that lands
/// entirely past the Valued phase's total. See SqlServerCompiledSearchService.SearchStreamWithPhaseHandlingAsync
/// for the algorithm these tests exercise.
/// </summary>
// CA1001 (owns disposable fields but isn't itself IDisposable): mirrors SqlServerCompiledSearchServiceTests.cs's
// own suppression rationale -- xunit already drives this type's lifecycle through IAsyncLifetime, and
// DisposeAsync below disposes every disposable field.
#pragma warning disable CA1001
public class SqlServerCompiledSearchServiceSortTests : IAsyncLifetime
#pragma warning restore CA1001
{
    private TestTenantDatabase _database = null!;
    private SqlServerSearchIndexReferenceDataCache _searchCache = null!;
    private SqlServerCompiledSearchService _service = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();

        // dbo.SearchParam has no seed data of its own (see TestTenantDatabase.SeedResourceTypeAsync's
        // remarks on dbo.ResourceType's identical situation) -- the write path's row generators only
        // index a value when its SearchParamId is already present in the catalog (SearchParameterIdLookupHelper
        // silently skips an unknown one), so "family" must be seeded before the first CreateOrUpdateAsync
        // call, matching SqlServerSymbolResolverTests.cs's identical raw-INSERT pattern.
        await _database.ExecuteNonQueryAsync(
            "INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) " +
            "VALUES ('http://hl7.org/fhir/SearchParameter/individual-family', 'active', SYSDATETIMEOFFSET(), 0)");

        _searchCache = new SqlServerSearchIndexReferenceDataCache(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        await _searchCache.PreloadResourceTypesAsync(CancellationToken.None);
        var resolver = new SqlServerSymbolResolver(_searchCache);

        var compartmentDefinitionManager = new CompartmentDefinitionManager(FhirVersion.R4);
        var schemaProvider = FhirVersion.R4.GetSchemaProvider();
        var searchParameterDefinitionManager = new SearchParameterDefinitionManager(
            schemaProvider, NullLogger<SearchParameterDefinitionManager>.Instance);

        var compressor = new GzipResourceCompressor(new RecyclableMemoryStreamManager());

        _service = new SqlServerCompiledSearchService(
            _database.SqlExecutionService,
            _database.TenantId,
            resolver,
            compartmentDefinitionManager,
            searchParameterDefinitionManager,
            compressor,
            NullLogger.Instance);
    }

    public async Task DisposeAsync()
    {
        _searchCache.Dispose();
        await _database.DisposeAsync();
    }

    private static readonly SearchParameterInfo FamilyParameter = new(
        "family", "family", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/individual-family"));

    // "_id" is a resource-column key (no SearchParamId, no catalog seeding needed) -- constructed the
    // same way SqlServerCompiledSearchServiceTests.cs's own fixture does, rather than resolved through
    // a definition manager, which has no entry for it.
    private static readonly SearchParameterInfo IdParameter = new(
        "_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));

    private static readonly SortExpression FamilyAscending = new(FamilyParameter, SortOrder.Ascending);

    // The bare ResourceWrapper constructor leaves SearchIndices null -- production indexing
    // (CreateOrUpdateResourceHandler.CreateResourceWrapper) computes it via ISearchIndexer.Extract
    // BEFORE calling the repository. This test project doesn't wire a full ISearchIndexer into its
    // fixtures, so a hand-built SearchIndexEntry/StringSearchValue is the established substitute --
    // SqlServerMergeRepositoryTests.cs and SqlServerFhirRepositoryCrudTests.cs already use this exact
    // pattern for the same reason. IsMin/IsMax are both set true because each patient here has a
    // single family value: ElementSearchIndexer.MarkMinMaxValues marks a lone value as both the min
    // and the max for its search parameter, and the Valued phase's join seeks on IsMin = 1.
    private async Task CreatePatientWithFamilyAsync(string resourceId, string family)
    {
        var familyValue = new StringSearchValue(family) { IsMin = true, IsMax = true };
        var resource = new ResourceWrapper(
            "Patient",
            resourceId,
            "1",
            DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{resourceId}}","name":[{"family":"{{family}}"}]}"""),
            new ResourceRequest("PUT", $"Patient/{resourceId}"))
        {
            SearchIndices = [new SearchIndexEntry(FamilyParameter, familyValue)]
        };

        await _database.Repository.CreateOrUpdateAsync(resource, CancellationToken.None);
    }

    private async Task CreatePatientWithoutFamilyAsync(string resourceId)
    {
        var resource = new ResourceWrapper(
            "Patient",
            resourceId,
            "1",
            DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{resourceId}}"}"""),
            new ResourceRequest("PUT", $"Patient/{resourceId}"));

        await _database.Repository.CreateOrUpdateAsync(resource, CancellationToken.None);
    }

    /// <summary>Creates 10 Patients with "family" set (Valued) followed by 5 without it (MissingPrimary).</summary>
    private async Task CreateValuedAndMissingPatientsAsync(string tag)
    {
        for (var i = 0; i < 10; i++)
        {
            await CreatePatientWithFamilyAsync($"sort-{tag}-valued-{i}", $"family-{i:D2}");
        }

        for (var i = 0; i < 5; i++)
        {
            await CreatePatientWithoutFamilyAsync($"sort-{tag}-missing-{i}");
        }

        await AssertFamilyIndexedIntoStringSearchParamAsync(tag);
    }

    /// <summary>
    /// Regression guard for the defect this fixture originally had: with SearchIndices left null,
    /// CreateOrUpdateAsync silently indexes nothing, the Valued phase's join always matches zero
    /// rows, and both tests below pass by numeric coincidence against a single unified
    /// MissingPrimary-only pool rather than a genuine Valued/MissingPrimary split. Fails fast with a
    /// diagnostic message instead of letting that regress silently.
    /// </summary>
    private async Task AssertFamilyIndexedIntoStringSearchParamAsync(string tag)
    {
        var familySearchParamId = await _database.ExecuteScalarAsync<int>(
            $"SELECT SearchParamId FROM dbo.SearchParam WHERE Uri = '{FamilyParameter.Url}'");

        var indexedRowCount = await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.StringSearchParam sp " +
            "INNER JOIN dbo.Resource r ON r.ResourceSurrogateId = sp.ResourceSurrogateId " +
            $"WHERE sp.SearchParamId = {familySearchParamId} AND r.ResourceId LIKE 'sort-{tag}-valued-%' AND r.IsHistory = 0");

        indexedRowCount.ShouldBe(
            10,
            "no rows in dbo.StringSearchParam for the family SearchParamId -- the 10 \"with family\" " +
            "patients weren't indexed, so the two-phase Valued/MissingPrimary split isn't being genuinely exercised.");
    }

    [Fact]
    public async Task GivenAPageStraddlingTheValuedMissingPrimaryBoundary_WhenSearchStreamAsyncCalled_ThenReturnsExactlyThePageWithNoDuplicatesOrGaps()
    {
        // Arrange -- create 10 Patients with a sortable String parameter set (Valued), then 5 more Patients
        // WITHOUT that parameter set (MissingPrimary). Sort ascending by that parameter, page size 5,
        // request offset=8. ProbeExtraRow makes the real requestedCount 6: Valued has only 2 rows left
        // from offset 8 (rows 8-9 of its 10), so Valued returns 2 and MissingPrimary fills the remaining
        // 6-2=4 at its own offset 0 (rows 0-3 of its 5) -- 2 + 4 = 6 rows total, straddling the phase
        // boundary with no duplicate and no gap.
        var tag = Guid.NewGuid().ToString("N");
        await CreateValuedAndMissingPatientsAsync(tag);

        var options = new SearchOptions
        {
            ResourceType = "Patient",
            Sort = [FamilyAscending],
            MaxItemCount = 5,
            ProbeExtraRow = true,
            ContinuationToken = ContinuationToken.Encode(offset: 8, count: 5),
        };

        // Act
        var results = new List<SearchEntryResult>();
        await foreach (var result in _service.SearchStreamAsync(options, CancellationToken.None))
        {
            results.Add(result);
        }

        // Assert -- exactly 6 rows (2 from the tail of Valued, 4 from the head of MissingPrimary, per the
        // probe-row arithmetic above), no duplicates against an adjacent page, no gap.
        results.Count.ShouldBe(6);
        results.Select(r => r.ResourceId).Distinct().Count().ShouldBe(6);
    }

    [Fact]
    public async Task GivenAPageEntirelyWithinMissingPrimary_WhenSearchStreamAsyncCalled_ThenComputesTheCorrectMissingPrimaryOffset()
    {
        // Arrange -- same 10 Valued + 5 MissingPrimary setup. Request offset=12, encoded count=5 (real
        // requestedCount, after +1, is 6) -- entirely past the Valued phase's 10 rows (Valued returns 0),
        // so a countPhaseScoped CountOnly compile reports the Valued total (10), giving MissingPrimary
        // offset max(0, 12-10)=2, limit 6-0=6. MissingPrimary only has 5 total rows, 3 of which remain
        // from its own offset 2 (rows 2, 3, 4) -- so min(6, 3) = 3 rows returned. The +1 convention doesn't
        // change this test's answer (data runs out at 3 either way, whether the limit is 5 or 6), but the
        // comment states the real 6 so a future reader isn't misled the way an earlier draft of this test
        // was.
        var tag = Guid.NewGuid().ToString("N");
        await CreateValuedAndMissingPatientsAsync(tag);

        var options = new SearchOptions
        {
            ResourceType = "Patient",
            Sort = [FamilyAscending],
            MaxItemCount = 5,
            ContinuationToken = ContinuationToken.Encode(offset: 12, count: 5),
        };

        // Act
        var results = new List<SearchEntryResult>();
        await foreach (var result in _service.SearchStreamAsync(options, CancellationToken.None))
        {
            results.Add(result);
        }

        // Assert -- exactly 3 rows (rows 12, 13, 14 of the combined 15).
        results.Count.ShouldBe(3);
    }

    /// <summary>
    /// The ordering consequence of the two-phase model: rows with no value for the sort key come
    /// LAST, not first, in an ascending sort. That is a deliberate divergence from SQL Server's own
    /// default (NULLs first ascending), so it cannot be inferred from the SQL -- it comes from the
    /// executor exhausting the Valued phase before touching MissingPrimary at all. The two tests
    /// above only count rows across the phase boundary; neither would notice the two phases being
    /// emitted in the opposite order.
    /// </summary>
    [Fact]
    public async Task GivenAnAscendingSortWithSomeResourcesMissingTheSortKey_WhenSearchStreamAsyncCalled_ThenTheMissingKeyRowsComeLast()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString("N");
        await CreatePatientWithFamilyAsync($"sort-{tag}-with-1", "Alpha");
        await CreatePatientWithFamilyAsync($"sort-{tag}-with-2", "Beta");
        await CreatePatientWithoutFamilyAsync($"sort-{tag}-without-1");
        await CreatePatientWithoutFamilyAsync($"sort-{tag}-without-2");

        var options = new SearchOptions { ResourceType = "Patient", Sort = [FamilyAscending] };

        // Act
        var results = await CollectAsync(options);

        // Assert
        results.Select(r => r.ResourceId).ShouldBe(
        [
            $"sort-{tag}-with-1",
            $"sort-{tag}-with-2",
            $"sort-{tag}-without-1",
            $"sort-{tag}-without-2",
        ]);
    }

    [Fact]
    public async Task GivenADescendingSortWhereEveryResourceHasTheSortKey_WhenSearchStreamAsyncCalled_ThenResultsAreInDescendingOrder()
    {
        // Arrange -- distinct, plain-ASCII values so there is no tie-break ambiguity.
        var tag = Guid.NewGuid().ToString("N");
        (string Suffix, string Family)[] patients =
        [
            ("apple", "Apple"), ("banana", "Banana"), ("cherry", "Cherry"), ("date", "Date"),
        ];
        foreach (var (suffix, family) in patients)
        {
            await CreatePatientWithFamilyAsync($"sort-{tag}-{suffix}", family);
        }

        var options = new SearchOptions
        {
            ResourceType = "Patient",
            Sort = [new SortExpression(FamilyParameter, SortOrder.Descending)],
        };

        // Act
        var results = await CollectAsync(options);

        // Assert
        results.Select(r => r.ResourceId).ShouldBe(
        [
            $"sort-{tag}-date",
            $"sort-{tag}-cherry",
            $"sort-{tag}-banana",
            $"sort-{tag}-apple",
        ]);
    }

    /// <summary>
    /// <c>_sort=_id</c> takes the <c>SortKeyKind.ResourceId</c> path -- a join against
    /// <c>dbo.Resource.ResourceId</c> rather than a search-index table -- and never produces a
    /// MissingPrimary phase, since every resource has an id. Ids here are lowercase-ASCII-only so the
    /// expected order is the same under any SQL Server collation.
    /// </summary>
    [Fact]
    public async Task GivenAnIdSort_WhenSearchStreamAsyncCalled_ThenResultsAreOrderedByResourceIdInBothDirections()
    {
        // Arrange -- created out of order, so a passing assertion cannot come from insertion order.
        var tag = Guid.NewGuid().ToString("N");
        foreach (var suffix in new[] { "c", "a", "e", "b", "d" })
        {
            await CreatePatientWithoutFamilyAsync($"idsort-{tag}-{suffix}");
        }

        var ascendingOptions = new SearchOptions
        {
            ResourceType = "Patient",
            Sort = [new SortExpression(IdParameter, SortOrder.Ascending)],
        };
        var descendingOptions = new SearchOptions
        {
            ResourceType = "Patient",
            Sort = [new SortExpression(IdParameter, SortOrder.Descending)],
        };

        // Act
        var ascending = await CollectAsync(ascendingOptions);
        var descending = await CollectAsync(descendingOptions);

        // Assert
        string[] expectedAscending =
        [
            $"idsort-{tag}-a", $"idsort-{tag}-b", $"idsort-{tag}-c", $"idsort-{tag}-d", $"idsort-{tag}-e",
        ];
        ascending.Select(r => r.ResourceId).ShouldBe(expectedAscending);
        descending.Select(r => r.ResourceId).ShouldBe(expectedAscending.Reverse());
    }

    /// <summary>
    /// Offset paging through the real <c>ContinuationToken</c> API, mirroring the Application layer's
    /// own convention: request <c>pageSize + 1</c> to detect hasMore, render only the first
    /// <c>pageSize</c> entries, and encode the next token as
    /// <c>ContinuationToken.Encode(currentOffset + pageSize, pageSize)</c> -- the ORIGINAL page size,
    /// not the +1'd one (see <c>StreamingBundleSerializer.SerializeWithPaginationAsync</c>). The two
    /// tests at the top of this file page within a Valued/MissingPrimary split; this one pages a
    /// single-phase result set and asserts the exact contents of both pages.
    /// </summary>
    [Fact]
    public async Task GivenOffsetPagingAcrossAPageBoundary_WhenBothPagesAreFetched_ThenTogetherTheyCoverEveryResourceExactlyOnceInOrder()
    {
        // Arrange
        const int PageSize = 3;
        var tag = Guid.NewGuid().ToString("N");
        string[] suffixes = ["a", "b", "c", "d", "e", "f"];
        foreach (var suffix in suffixes)
        {
            await CreatePatientWithoutFamilyAsync($"page-{tag}-{suffix}");
        }

        var page1Options = new SearchOptions
        {
            ResourceType = "Patient",
            Sort = [new SortExpression(IdParameter, SortOrder.Ascending)],
            MaxItemCount = PageSize,
            ProbeExtraRow = true,
        };

        // Act
        var page1Raw = await CollectAsync(page1Options);

        var page2Options = new SearchOptions
        {
            ResourceType = "Patient",
            Sort = [new SortExpression(IdParameter, SortOrder.Ascending)],
            MaxItemCount = PageSize,
            ProbeExtraRow = true,
            ContinuationToken = ContinuationToken.Encode(offset: PageSize, count: PageSize),
        };
        var page2Raw = await CollectAsync(page2Options);

        // Assert -- page 1 signals hasMore by returning more than PageSize rows; the rendered page is
        // the first PageSize of them.
        page1Raw.Count.ShouldBe(PageSize + 1);
        page1Raw.Take(PageSize).Select(r => r.ResourceId).ShouldBe(
            [$"page-{tag}-a", $"page-{tag}-b", $"page-{tag}-c"]);

        page2Raw.Take(PageSize).Select(r => r.ResourceId).ShouldBe(
            [$"page-{tag}-d", $"page-{tag}-e", $"page-{tag}-f"]);
        page2Raw.Count.ShouldBe(PageSize, "page 2 is the last page: there is no seventh row to signal hasMore with.");
    }

    private async Task<List<SearchEntryResult>> CollectAsync(SearchOptions options)
    {
        var results = new List<SearchEntryResult>();
        await foreach (var result in _service.SearchStreamAsync(options, CancellationToken.None))
        {
            results.Add(result);
        }

        return results;
    }
}
