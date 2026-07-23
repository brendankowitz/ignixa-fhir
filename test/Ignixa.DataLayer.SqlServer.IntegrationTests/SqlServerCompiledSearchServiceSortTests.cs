using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.DataLayer.SqlServer.Search;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
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

    private static readonly SortExpression FamilyAscending = new(FamilyParameter, SortOrder.Ascending);

    private async Task CreatePatientWithFamilyAsync(string resourceId, string family)
    {
        var resource = new ResourceWrapper(
            "Patient",
            resourceId,
            "1",
            DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{resourceId}}","name":[{"family":"{{family}}"}]}"""),
            new ResourceRequest("PUT", $"Patient/{resourceId}"));

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
    }

    [Fact]
    public async Task GivenAPageStraddlingTheValuedMissingPrimaryBoundary_WhenSearchStreamAsyncCalled_ThenReturnsExactlyThePageWithNoDuplicatesOrGaps()
    {
        // Arrange -- create 10 Patients with a sortable String parameter set (Valued), then 5 more Patients
        // WITHOUT that parameter set (MissingPrimary). Sort ascending by that parameter, page size 5,
        // request offset=8. The token encodes count=5, but the +1-for-hasMore convention makes the real
        // requestedCount 6: Valued has only 2 rows left from offset 8 (rows 8-9 of its 10), so Valued
        // returns 2 and MissingPrimary fills the remaining 6-2=4 at its own offset 0 (rows 0-3 of its 5) --
        // 2 + 4 = 6 rows total, straddling the phase boundary with no duplicate and no gap.
        var tag = Guid.NewGuid().ToString("N");
        await CreateValuedAndMissingPatientsAsync(tag);

        var options = new SearchOptions
        {
            ResourceType = "Patient",
            Sort = [FamilyAscending],
            MaxItemCount = 5,
            ContinuationToken = ContinuationToken.Encode(offset: 8, count: 5),
        };

        // Act
        var results = new List<SearchEntryResult>();
        await foreach (var result in _service.SearchStreamAsync(options, CancellationToken.None))
        {
            results.Add(result);
        }

        // Assert -- exactly 6 rows (2 from the tail of Valued, 4 from the head of MissingPrimary, per the
        // +1-for-hasMore arithmetic above), no duplicates against an adjacent page, no gap.
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
}
