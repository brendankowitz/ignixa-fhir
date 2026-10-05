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

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Indexing;

/// <summary>
/// Proves the consequence of a poisoned search-parameter cache entry where it actually bites: the index
/// rows in <c>dbo.*SearchParam</c>, not the cache dictionary. A URI probed before its <c>dbo.SearchParam</c>
/// row exists is remembered missing for the process lifetime (search parameters have no TTL and no
/// invalidation), <c>SentinelFilteringDictionary.TryGetValue</c> reports the sentinel as absent, and every
/// row generator then skips that parameter's rows -- while the write itself still reports success.
/// <para>
/// <see cref="InitializeAsync"/> must preload search parameters, exactly as <c>SqlServerRepositoryFactory</c>
/// does at cache creation. That is what makes the poisoning permanent: <c>MergeResourcesAsync</c> calls
/// <c>EnsureSearchParametersPreloadedAsync</c> on every merge, so on a cache that has never loaded, the first
/// merge re-reads the whole table and incidentally heals the sentinel. Once the load flag is set that
/// self-healing is gone for the rest of the process, and this sync method is the only remaining repair.
/// </para>
/// </summary>
// CA1001 (owns a disposable field but isn't itself IDisposable): matches SqlServerMergeRepositoryTests'
// rationale -- the cache's only disposable is a SemaphoreSlim, explicitly disposed in DisposeAsync below,
// and xunit's IAsyncLifetime already drives this class's lifecycle.
#pragma warning disable CA1001
public class SqlServerSearchParameterSyncIndexRowTests : IAsyncLifetime
#pragma warning restore CA1001
{
    private const string IdentifierSearchParamUrl = "http://hl7.org/fhir/SearchParameter/Patient-identifier";

    private TestTenantDatabase _database = null!;
    private SqlServerSearchIndexReferenceDataCache _cache = null!;
    private SqlServerMergeRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateEmptyAsync();
        _cache = new SqlServerSearchIndexReferenceDataCache(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        await _cache.PreloadResourceTypesAsync(CancellationToken.None);
        await _cache.PreloadSearchParamsAsync(maxRows: null, CancellationToken.None);
        var compressor = new GzipResourceCompressor(new RecyclableMemoryStreamManager());
        var extensionUpdater = new SqlServerPostMergeExtensionUpdater(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerPostMergeExtensionUpdater>.Instance);
        _repository = new SqlServerMergeRepository(
            _database.SqlExecutionService, _database.TenantId, compressor, _cache, extensionUpdater,
            NullLogger<SqlServerMergeRepository>.Instance);
    }

    public async Task DisposeAsync()
    {
        _cache.Dispose();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task GivenAProbeThatCachedTheNotFoundSentinel_WhenTheParameterIsSyncedAndAResourceIsIndexed_ThenItsIndexRowsAreWritten()
    {
        // Arrange: probing before the row exists is what poisons the cache. Without a repair surface this
        // is terminal -- the row generator drops this parameter's rows for every resource this process
        // writes from here on, and the write still reports success.
        (await _cache.GetSearchParamIdAsync(IdentifierSearchParamUrl, CancellationToken.None)).ShouldBeNull();

        var syncedCount = await _cache.SyncSearchParametersToDatabaseAsync(
            [IdentifierSearchParamUrl], searchParameterDefinitionManager: null, CancellationToken.None);
        syncedCount.ShouldBe(1);

        // Act
        var transactionId = await WritePatientWithIdentifierAsync("patient-after-sync", "12345");

        // Assert: the row generator resolved a real SearchParamId, so the index row landed. Pre-fix -- with
        // the dbo.SearchParam row created by any means other than this sync -- this count is 0.
        var rowCount = await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.TokenSearchParam WHERE ResourceSurrogateId >= {transactionId}");
        rowCount.ShouldBe(
            1,
            "a synced search parameter must produce index rows -- a cached not-found sentinel silently drops them");
    }

    [Fact]
    public async Task GivenASearchParameterRowCreatedOutsideTheCache_WhenTheParameterIsSynced_ThenTheCachedSentinelStopsDroppingIndexRows()
    {
        // Arrange: the row exists, but it was created without going through the cache -- exactly what
        // happens when another process or a raw migration seeds dbo.SearchParam after this process probed.
        // Creating the row is NOT sufficient: nothing invalidates the sentinel this probe cached.
        (await _cache.GetSearchParamIdAsync(IdentifierSearchParamUrl, CancellationToken.None)).ShouldBeNull();
        await _database.ExecuteNonQueryAsync(
            "INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) " +
            $"VALUES ('{IdentifierSearchParamUrl}', 'Enabled', SYSDATETIMEOFFSET(), 0)");

        var droppedTransactionId = await WritePatientWithIdentifierAsync("patient-row-only", "row-only");
        var droppedRowCount = await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.TokenSearchParam WHERE ResourceSurrogateId >= {droppedTransactionId}");
        droppedRowCount.ShouldBe(
            0,
            "characterizes the data loss: the row exists and the write succeeded, yet the index row was dropped");

        // Act
        var syncedCount = await _cache.SyncSearchParametersToDatabaseAsync(
            [IdentifierSearchParamUrl], searchParameterDefinitionManager: null, CancellationToken.None);

        // Assert: the row already existed, so nothing was inserted (hence 0), but the sentinel was replaced.
        syncedCount.ShouldBe(0);
        var repairedTransactionId = await WritePatientWithIdentifierAsync("patient-after-repair", "repaired");
        var repairedRowCount = await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.TokenSearchParam WHERE ResourceSurrogateId >= {repairedTransactionId}");
        repairedRowCount.ShouldBe(1, "syncing must overwrite the sentinel so index rows resume landing");
    }

    private async Task<long> WritePatientWithIdentifierAsync(string resourceId, string identifierValue)
    {
        var (transactionId, _) = await _repository.BeginTransactionAsync(resourceCount: 1, CancellationToken.None);

        var resourceJson = ResourceJsonNode.Parse(
            $$"""{"resourceType":"Patient","id":"{{resourceId}}","identifier":[{"value":"{{identifierValue}}"}]}""");
        var searchParameter = new SearchParameterInfo(
            "identifier", "identifier", SearchParamType.Token, new Uri(IdentifierSearchParamUrl));
        var tokenValue = new TokenSearchValue(
            system: null, code: identifierValue, text: null, identifierTypeSystem: null, identifierTypeCode: null);

        var wrapper = new ResourceWrapper(
            "Patient", resourceId, "1", DateTimeOffset.UtcNow, resourceJson,
            new ResourceRequest("PUT", $"Patient/{resourceId}"))
        {
            SearchIndices = [new SearchIndexEntry(searchParameter, tokenValue)],
        };

        await _repository.MergeResourcesAsync(
            transactionId, singleTransaction: true, [wrapper], [0], CancellationToken.None);
        await _repository.CommitTransactionAsync(transactionId, cancellationToken: CancellationToken.None);

        return transactionId;
    }
}
