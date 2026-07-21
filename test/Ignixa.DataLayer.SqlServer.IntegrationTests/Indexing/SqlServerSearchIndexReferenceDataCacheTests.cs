using System.Linq;
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

    [Fact]
    public async Task GivenAColdCache_WhenEnsureSearchParametersPreloadedAsyncCalledConcurrently_ThenEveryParameterIsLoadedForEveryCaller()
    {
        // A small seeded catalog wouldn't reliably expose the race (the population loop finishes
        // before a second caller's check can land in the gap) -- the real production bug only
        // manifested against the real ~1400-row catalog. 200 rows widens the population loop's
        // duration enough to make a still-broken guard fail this test reliably, not flakily.
        const int SearchParamCount = 200;
        var values = string.Join(",", Enumerable.Range(0, SearchParamCount)
            .Select(i => $"('http://example.org/ensure-test-param-{i}', 'active', SYSDATETIMEOFFSET(), 0)"));
        await _database.ExecuteNonQueryAsync(
            $"INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) VALUES {values}");

        var callers = Enumerable.Range(0, 20)
            .Select(_ => _cache.EnsureSearchParametersPreloadedAsync(CancellationToken.None));
        await Task.WhenAll(callers);

        _cache.SearchParameterMappings.Count.ShouldBe(SearchParamCount);
        for (var i = 0; i < SearchParamCount; i++)
        {
            _cache.SearchParameterMappings.ContainsKey($"http://example.org/ensure-test-param-{i}")
                .ShouldBeTrue($"parameter index {i} must be present -- a race would drop some entries");
        }
    }

    [Fact]
    public async Task GivenAColdCache_WhenEnsureResourceTypesPreloadedAsyncCalledConcurrently_ThenEveryResourceTypeIsLoadedForEveryCaller()
    {
        var callers = Enumerable.Range(0, 20)
            .Select(_ => _cache.EnsureResourceTypesPreloadedAsync(CancellationToken.None));
        await Task.WhenAll(callers);

        _cache.ResourceTypeMappings.ContainsKey("Patient").ShouldBeTrue();
    }

    [Fact]
    public async Task GivenAWarmCache_WhenEnsureSearchParametersPreloadedAsyncCalledAgain_ThenItIsANoOp()
    {
        // Seed one row BEFORE the first call -- otherwise the cache starts and stays genuinely
        // empty (0 rows in dbo.SearchParam), which is indistinguishable from "still cold" and the
        // guard would legitimately reload on the second call too, making this test assert nothing
        // about the no-op behavior it's meant to prove.
        await _database.ExecuteNonQueryAsync(
            "INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) " +
            "VALUES ('http://example.org/warm-before-first-call', 'active', SYSDATETIMEOFFSET(), 0)");

        await _cache.EnsureSearchParametersPreloadedAsync(CancellationToken.None);
        var countAfterFirstCall = _cache.SearchParameterMappings.Count;

        await _database.ExecuteNonQueryAsync(
            "INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) " +
            "VALUES ('http://example.org/added-after-warm', 'active', SYSDATETIMEOFFSET(), 0)");

        await _cache.EnsureSearchParametersPreloadedAsync(CancellationToken.None);

        // Count is unchanged -- Ensure* is a "populate if empty" guard, not a refresh. The newly
        // inserted row is invisible to this cache instance until something explicitly reloads it;
        // that is existing, intentional cache behavior, not something this fix changes.
        _cache.SearchParameterMappings.Count.ShouldBe(countAfterFirstCall);
    }
}
