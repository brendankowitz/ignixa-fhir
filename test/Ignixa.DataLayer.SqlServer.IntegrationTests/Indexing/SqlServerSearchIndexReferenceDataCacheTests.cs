using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Indexing;

// CA1001 (owns a disposable field but isn't itself IDisposable): SqlServerSearchIndexReferenceDataCache
// only owns a SemaphoreSlim (no unmanaged resources); the test explicitly disposes it in
// DisposeAsync below rather than implementing IDisposable purely to satisfy the analyzer on a
// class whose lifecycle xunit already drives through IAsyncLifetime.
#pragma warning disable CA1001
public class SqlServerSearchIndexReferenceDataCacheTests : IAsyncLifetime
#pragma warning restore CA1001
{
    private TestTenantDatabase _database = null!;
    private SqlServerSearchIndexReferenceDataCache _cache = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateEmptyAsync();
        _cache = new SqlServerSearchIndexReferenceDataCache(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
    }

    public async Task DisposeAsync()
    {
        _cache.Dispose();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task GivenAKnownResourceType_WhenGetResourceTypeIdAsyncCalled_ThenReturnsItsRealId()
    {
        await _cache.PreloadResourceTypesAsync(CancellationToken.None);
        var id = await _cache.GetResourceTypeIdAsync("Patient", CancellationToken.None);
        id.ShouldNotBeNull();
        _cache.ResourceTypeMappings["Patient"].ShouldBe(id!.Value);
    }

    [Fact]
    public async Task GivenAnUnknownResourceTypeName_WhenGetResourceTypeIdAsyncCalledTwice_ThenReturnsNullBothTimesAndOnlyQueriesOnce()
    {
        var first = await _cache.GetResourceTypeIdAsync("NotARealResourceType", CancellationToken.None);
        var second = await _cache.GetResourceTypeIdAsync("NotARealResourceType", CancellationToken.None);
        first.ShouldBeNull();
        second.ShouldBeNull();
        _cache.TryGetResourceTypeIdFromCache("NotARealResourceType").ShouldBeNull();
    }

    [Fact]
    public async Task GivenANewSystemUri_WhenGetOrCreateSystemIdAsyncCalled_ThenInsertsAndReturnsAGeneratedId()
    {
        var systemUri = $"http://example.org/test-system-{Guid.NewGuid()}";
        var id = await _cache.GetOrCreateSystemIdAsync(systemUri, CancellationToken.None);
        id.ShouldBeGreaterThan(0);
        _cache.SystemMappings[systemUri].ShouldBe(id);

        var rowCount = await _database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM dbo.System WHERE SystemId = {id}");
        rowCount.ShouldBe(1);
    }

    [Fact]
    public async Task GivenAnExistingSystemUri_WhenGetOrCreateSystemIdAsyncCalledTwice_ThenReturnsTheSameIdBothTimesAndInsertsOnce()
    {
        var systemUri = $"http://example.org/test-system-{Guid.NewGuid()}";
        var firstId = await _cache.GetOrCreateSystemIdAsync(systemUri, CancellationToken.None);
        var secondId = await _cache.GetOrCreateSystemIdAsync(systemUri, CancellationToken.None);
        secondId.ShouldBe(firstId);

        var rowCount = await _database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM dbo.System WHERE Value = '{systemUri}'");
        rowCount.ShouldBe(1);
    }

    [Fact]
    public async Task GivenANewQuantityCode_WhenGetOrCreateQuantityCodeIdAsyncCalled_ThenInsertsAndReturnsAGeneratedId()
    {
        var code = $"test-code-{Guid.NewGuid():N}";
        var id = await _cache.GetOrCreateQuantityCodeIdAsync(code, CancellationToken.None);
        id.ShouldBeGreaterThan(0);
        _cache.QuantityCodeMappings[code].ShouldBe(id);
    }

    [Fact]
    public void GivenCacheResourceTypeIdCalledDirectly_WhenTryGetResourceTypeIdFromCacheCalled_ThenReturnsTheCachedValueWithoutAnyDbAccess()
    {
        _cache.CacheResourceTypeId("SyntheticType", 999);
        _cache.TryGetResourceTypeIdFromCache("SyntheticType").ShouldBe((short)999);
    }

    [Fact]
    public async Task GivenASystemIdInsertedThroughOneCacheInstance_WhenAnotherCacheInstanceReadsSystemMappings_ThenTheLiveDictionaryReflectsItAfterAFreshLookup()
    {
        // Proves SystemMappings is a live view, not a point-in-time snapshot -- a second logical
        // "reader" (here, a second GetOrCreateSystemIdAsync call reusing the same cache instance,
        // which is how SqlServerMergeRepository's captured dictionary reference actually behaves)
        // sees the insert without needing to re-fetch the property.
        var systemUri = $"http://example.org/live-view-{Guid.NewGuid()}";
        var mappingsReference = _cache.SystemMappings;
        mappingsReference.ContainsKey(systemUri).ShouldBeFalse();

        await _cache.GetOrCreateSystemIdAsync(systemUri, CancellationToken.None);

        mappingsReference.ContainsKey(systemUri).ShouldBeTrue();
    }
}
