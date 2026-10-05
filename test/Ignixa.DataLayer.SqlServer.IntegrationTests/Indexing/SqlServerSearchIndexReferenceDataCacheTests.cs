using System.Collections.Concurrent;
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
    public async Task GivenAnUnknownResourceTypeName_WhenGetResourceTypeIdAsyncCalledTwice_ThenReturnsNullBothTimesAndRemembersNothing()
    {
        // Each call re-queries by design: dbo.ResourceType is populated as types are first encountered, so
        // remembering "absent" would answer wrongly for every later lookup and write of that type once it
        // exists. See GetResourceTypeIdAsync's miss branch.
        var first = await _cache.GetResourceTypeIdAsync("NotARealResourceType", CancellationToken.None);
        var second = await _cache.GetResourceTypeIdAsync("NotARealResourceType", CancellationToken.None);
        first.ShouldBeNull();
        second.ShouldBeNull();
        _cache.TryGetResourceTypeIdFromCache("NotARealResourceType").ShouldBeNull();
        _cache.ResourceTypeMappings.ContainsKey("NotARealResourceType").ShouldBeFalse();
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
        // This validates correctness and completeness under real concurrent load at a
        // production-representative scale (20 concurrent callers, 200 rows) -- it does NOT
        // reliably force a reader into the narrow mid-population-loop race window (that in-memory
        // insert loop completes on a microsecond scale, so a still-broken guard can pass this test
        // by sheer timing luck; see GivenALoadPausedMidPopulation_... below for the deterministic
        // reproduction of that specific race).
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

    [Fact]
    public async Task GivenALoadPausedMidPopulation_WhenASecondCallerInvokesEnsureSearchParametersPreloadedAsyncConcurrently_ThenItBlocksUntilTheLoadCompletesAndNeverObservesAPartialMap()
    {
        const int SearchParamCount = 10;
        var values = string.Join(",", Enumerable.Range(0, SearchParamCount)
            .Select(i => $"('http://example.org/paused-load-param-{i}', 'active', SYSDATETIMEOFFSET(), 0)"));
        await _database.ExecuteNonQueryAsync(
            $"INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) VALUES {values}");

        var firstRowInserted = new TaskCompletionSource();
        var releaseLoad = new TaskCompletionSource();
        var rowsSeenSoFar = 0;

        _cache.TestSearchParamRowInsertedHookAsync = async () =>
        {
            var seen = Interlocked.Increment(ref rowsSeenSoFar);
            if (seen == 1)
            {
                firstRowInserted.SetResult();
                await releaseLoad.Task;
            }
        };

        var firstCallerTask = _cache.EnsureSearchParametersPreloadedAsync(CancellationToken.None);

        // Wait until the first caller has inserted exactly one row and is paused inside the loop --
        // this is the exact window the original bug escaped through.
        await firstRowInserted.Task;

        var secondCallerTask = _cache.EnsureSearchParametersPreloadedAsync(CancellationToken.None);

        // Give the second caller every opportunity to race ahead if it were going to (the pre-fix
        // guard would return here immediately, before the load finishes).
        var completedEarly = await Task.WhenAny(secondCallerTask, Task.Delay(200)) == secondCallerTask;
        completedEarly.ShouldBeFalse(
            "the second caller must block until the first caller's load finishes -- returning early " +
            "here means it would go on to read a partially-populated cache, reproducing the original bug");

        releaseLoad.SetResult();
        await firstCallerTask;
        await secondCallerTask;

        _cache.SearchParameterMappings.Count.ShouldBe(SearchParamCount);
    }

    [Fact]
    public async Task GivenAColdSystemMappingsCache_WhenTryGetValueMissesOnAnUnknownSystemUri_ThenItResolvesInsertsAndCachesTheNewSystem()
    {
        var systemUri = $"http://example.org/self-heal-system-{Guid.NewGuid()}";

        var found = _cache.SystemMappings.TryGetValue(systemUri, out var resolvedId);

        found.ShouldBeTrue();
        resolvedId.ShouldBeGreaterThan(0);

        var rowCount = await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.System WHERE SystemId = {resolvedId}");
        rowCount.ShouldBe(1);

        // Second lookup on the SAME cache instance must hit the now-warm dictionary, not re-resolve.
        var secondLookupFound = _cache.SystemMappings.TryGetValue(systemUri, out var secondResolvedId);
        secondLookupFound.ShouldBeTrue();
        secondResolvedId.ShouldBe(resolvedId);
    }

    [Fact]
    public async Task GivenAColdQuantityCodeMappingsCache_WhenTryGetValueMissesOnAnUnknownCode_ThenItResolvesInsertsAndCachesTheNewCode()
    {
        var code = $"self-heal-code-{Guid.NewGuid():N}";

        var found = _cache.QuantityCodeMappings.TryGetValue(code, out var resolvedId);

        found.ShouldBeTrue();
        resolvedId.ShouldBeGreaterThan(0);

        var rowCount = await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.QuantityCode WHERE QuantityCodeId = {resolvedId}");
        rowCount.ShouldBe(1);
    }

    /// <summary>
    /// A failed resolver used to be caught and turned into a logged warning plus a <c>false</c> return --
    /// silently dropping the search-index row a caller was about to build from this dictionary, while
    /// <c>SqlServerMergeRepository.MergeResourcesAsync</c> went on to report a successful write. GetOrCreate*
    /// has no "genuinely not in the catalog" outcome (it always inserts), so a failed resolve here is always
    /// an infrastructure fault, never a legitimate miss -- it must propagate instead. See the remarks on
    /// <see cref="SqlServerSearchIndexReferenceDataCache.OnDemandResolvingDictionary{TKey,TValue}"/>.
    /// </summary>
    [Fact]
    public void GivenAResolverThatThrows_WhenTryGetValueMisses_ThenTheFailurePropagatesAndNothingIsCached()
    {
        var backingCache = new ConcurrentDictionary<string, int>();
        var wrapper = new SqlServerSearchIndexReferenceDataCache.OnDemandResolvingDictionary<string, int>(
            backingCache,
            (_, _) => Task.FromException<int>(new InvalidOperationException("simulated resolve failure")),
            -1);

        Should.Throw<InvalidOperationException>(() => wrapper.TryGetValue("any-key", out _))
            .Message.ShouldBe("simulated resolve failure");
        backingCache.ContainsKey("any-key").ShouldBeFalse();
    }
}
