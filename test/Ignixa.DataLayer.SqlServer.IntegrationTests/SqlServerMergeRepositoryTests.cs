using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

// CA1001 (owns a disposable field but isn't itself IDisposable): matches
// SqlServerSearchIndexReferenceDataCacheTests' rationale -- the cache's only disposable is a
// SemaphoreSlim (no unmanaged resources), and it is explicitly disposed in DisposeAsync below;
// xunit's IAsyncLifetime already drives this class's lifecycle.
#pragma warning disable CA1001
public class SqlServerMergeRepositoryTests : IAsyncLifetime
#pragma warning restore CA1001
{
    private TestTenantDatabase _database = null!;
    private SqlServerSearchIndexReferenceDataCache _cache = null!;
    private SqlServerMergeRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateEmptyAsync();
        _cache = new SqlServerSearchIndexReferenceDataCache(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        await _cache.PreloadResourceTypesAsync(CancellationToken.None);
        var compressor = new GzipResourceCompressor(new RecyclableMemoryStreamManager());
        var extensionUpdater = new SqlServerPostMergeExtensionUpdater(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerPostMergeExtensionUpdater>.Instance);
        _repository = new SqlServerMergeRepository(
            _database.SqlExecutionService, _database.TenantId, compressor, _cache, extensionUpdater, NullLogger<SqlServerMergeRepository>.Instance);
    }

    public async Task DisposeAsync()
    {
        _cache.Dispose();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task GivenASingleResource_WhenMergedThroughBeginMergeCommit_ThenARowExistsInDboResource()
    {
        var (transactionId, _) = await _repository.BeginTransactionAsync(resourceCount: 1, CancellationToken.None);

        var resourceJson = ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"test-patient-1"}""");
        var wrapper = new ResourceWrapper(
            "Patient", "test-patient-1", "1", DateTimeOffset.UtcNow, resourceJson,
            new ResourceRequest("PUT", "Patient/test-patient-1"));

        var affectedRows = await _repository.MergeResourcesAsync(
            transactionId, singleTransaction: true, [wrapper], [0], CancellationToken.None);
        await _repository.CommitTransactionAsync(transactionId, cancellationToken: CancellationToken.None);

        affectedRows.ShouldBeGreaterThan(0);
        var rowCount = await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = 'test-patient-1'");
        rowCount.ShouldBe(1);
    }

    [Fact]
    public async Task GivenAHeartbeatCall_WhenPutTransactionHeartbeatAsyncCalled_ThenTheTransactionsHeartbeatDateAdvances()
    {
        var (transactionId, _) = await _repository.BeginTransactionAsync(resourceCount: 1, CancellationToken.None);
        // dbo.Transactions.HeartbeatDate is DATETIME (not DATETIMEOFFSET) -- see
        // Ignixa.DataLayer.SqlServer.Database/Tables/Transactions.sql.
        var before = await _database.ExecuteScalarAsync<DateTime>(
            $"SELECT HeartbeatDate FROM dbo.Transactions WHERE SurrogateIdRangeFirstValue = {transactionId}");

        await Task.Delay(50);
        await _repository.PutTransactionHeartbeatAsync(transactionId, CancellationToken.None);

        var after = await _database.ExecuteScalarAsync<DateTime>(
            $"SELECT HeartbeatDate FROM dbo.Transactions WHERE SurrogateIdRangeFirstValue = {transactionId}");
        after.ShouldBeGreaterThan(before);
    }

    [Fact]
    public async Task GivenAResourceWithATokenIdentifierSearchIndex_WhenMergedAndTheSearchIndexIncludesAnIdentifierType_ThenTheExtensionColumnsAreWritten()
    {
        // Proves the extension-updater wiring (this task's own correction, per the plan review) is
        // real, not just present in the constructor -- IdentifierTypeSystemId/IdentifierTypeCode
        // are NOT writable through the TVP itself (CLAUDE.md's PostMergeExtensionUpdater pattern),
        // only via this post-merge call.
        //
        // TokenSearchParameterRowGenerator.GenerateSqlDataRecords/ExtractExtensionData expect
        // resource.SearchIndices to contain real Ignixa.Search.Indexing.SearchIndexEntry instances
        // wrapping an Ignixa.Search.Indexing.SearchValues.TokenSearchValue (see RowGenerators/
        // TokenSearchParameterRowGenerator.cs). The token's own System is left null here so the
        // core TVP row generation doesn't require a pre-populated System cache entry -- only
        // IdentifierTypeCode (which ExtractExtensionData always yields regardless of whether the
        // IdentifierTypeSystem URI resolves) is what this test asserts on.
        const string SearchParamUrl = "http://hl7.org/fhir/SearchParameter/Patient-identifier";
        await _database.ExecuteNonQueryAsync(
            $"INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) VALUES ('{SearchParamUrl}', 'active', SYSDATETIMEOFFSET(), 0)");

        var (transactionId, _) = await _repository.BeginTransactionAsync(resourceCount: 1, CancellationToken.None);
        var resourceJson = ResourceJsonNode.Parse(
            """{"resourceType":"Patient","id":"test-patient-identifier","identifier":[{"system":"http://example.org/mrn","value":"12345","type":{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/v2-0203","code":"MR"}]}}]}""");

        var searchParameter = new SearchParameterInfo(
            "identifier", "identifier", SearchParamType.Token, new Uri(SearchParamUrl));
        var tokenValue = new TokenSearchValue(
            system: null,
            code: "12345",
            text: null,
            identifierTypeSystem: "http://terminology.hl7.org/CodeSystem/v2-0203",
            identifierTypeCode: "MR");

        var wrapper = new ResourceWrapper(
            "Patient", "test-patient-identifier", "1", DateTimeOffset.UtcNow, resourceJson,
            new ResourceRequest("PUT", "Patient/test-patient-identifier"))
        {
            SearchIndices = [new SearchIndexEntry(searchParameter, tokenValue)]
        };

        await _repository.MergeResourcesAsync(transactionId, singleTransaction: true, [wrapper], [0], CancellationToken.None);
        await _repository.CommitTransactionAsync(transactionId, cancellationToken: CancellationToken.None);

        var identifierTypeCode = await _database.ExecuteScalarAsync<string>(
            $"SELECT TOP (1) IdentifierTypeCode FROM dbo.TokenSearchParam WHERE ResourceSurrogateId >= {transactionId}");
        identifierTypeCode.ShouldBe("MR");
    }
}
