using Ignixa.DataLayer.SqlServer.Features.Terminology;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Indexing;

/// <summary>
/// Ports the behavioural oracle from <c>SearchIndexReferenceDataCacheRegressionTests</c>
/// (Ignixa.DataLayer.SqlEntityFramework.IntegrationTests) onto the ADO.NET cache, plus the
/// out-of-band-creation cases that oracle has no equivalent for: the EF cache bounds a recorded miss with a
/// TTL, and rows created by another process are the reason that TTL exists.
/// </summary>
#pragma warning disable CA1001
public class SqlServerSearchIndexReferenceDataCacheNegativeLookupTests : IAsyncLifetime
#pragma warning restore CA1001
{
    private TestTenantDatabase _database = null!;
    private SqlServerSearchIndexReferenceDataCache _cache = null!;
    private TestTimeProvider _time = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateEmptyAsync();
        _time = new TestTimeProvider();
        _cache = new SqlServerSearchIndexReferenceDataCache(
            _database.SqlExecutionService,
            _database.TenantId,
            NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance,
            _time);
    }

    public async Task DisposeAsync()
    {
        _cache.Dispose();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task GivenASearchRecordedASystemMissing_WhenSqlServerSystemRepositoryCreatesIt_ThenTheSearchStopsReportingItMissing()
    {
        // Arrange
        var systemUri = $"http://imported.example/CodeSystem/{Guid.NewGuid():N}";
        (await _cache.TryGetSystemIdAsync(systemUri, CancellationToken.None)).ShouldBeNull();

        var repository = new SqlServerSystemRepository(_cache, NullLogger<SqlServerSystemRepository>.Instance);

        // Act
        var createdId = await repository.GetOrCreateAsync(systemUri, CancellationToken.None);

        // Assert
        (await _cache.TryGetSystemIdAsync(systemUri, CancellationToken.None)).ShouldBe(
            createdId,
            "creating the row must invalidate the recorded miss, whichever writer created it");
    }

    [Fact]
    public async Task GivenAProbeThatCachedTheNotFoundSentinel_WhenSearchParametersArePreloadedAgain_ThenTheRealIdReplacesIt()
    {
        // Arrange: SqlServer has no SyncSearchParametersToDatabase. PreloadSearchParamsAsync is the
        // equivalent repair path -- both write the real id over the sentinel through the indexer.
        var uri = $"http://example.org/SearchParameter/us-core-race-{Guid.NewGuid():N}";
        (await _cache.GetSearchParamIdAsync(uri, CancellationToken.None)).ShouldBeNull();

        await _database.ExecuteNonQueryAsync(
            "INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) " +
            $"VALUES ('{uri}', 'active', SYSDATETIMEOFFSET(), 0)");
        var realId = (short)await _database.ExecuteScalarAsync<int>(
            $"SELECT SearchParamId FROM dbo.SearchParam WHERE Uri = '{uri}'");

        // Act
        await _cache.PreloadSearchParamsAsync(maxRows: null, CancellationToken.None);

        // Assert
        _cache.TryGetSearchParamIdFromCache(uri).ShouldBe(
            realId,
            "a preloaded parameter must replace the cached not-found sentinel");
        (await _cache.GetSearchParamIdAsync(uri, CancellationToken.None)).ShouldBe(realId);
    }

    [Fact]
    public async Task GivenAProbeThatMissedAResourceType_WhenTheTypeIsLaterCreated_ThenTheLookupReturnsItsRealId()
    {
        // Arrange: dbo.ResourceType is populated as types are first encountered, so recording "absent"
        // for the process lifetime poisons every later lookup and write of that type.
        (await _cache.GetResourceTypeIdAsync("Measure", CancellationToken.None)).ShouldBeNull();

        await _database.ExecuteNonQueryAsync("INSERT INTO dbo.ResourceType (Name) VALUES ('Measure')");
        var realId = (short)await _database.ExecuteScalarAsync<int>(
            "SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name = 'Measure'");

        // Act
        var result = await _cache.GetResourceTypeIdAsync("Measure", CancellationToken.None);

        // Assert
        result.ShouldBe(realId);
    }

    [Fact]
    public async Task GivenAProbeThatRecordedASystemMissing_WhenTheRowIsCreatedOutOfBandAndTheTtlElapses_ThenTheLookupReturnsItsRealId()
    {
        // Arrange: an out-of-band INSERT stands in for the row a terminology import in another process or
        // another server instance creates. No in-process invalidation can observe it, so the recorded miss
        // expiring is the only thing that can recover -- before this fix the record had no lifetime at all
        // and the search answered "missing" for the rest of the process.
        var systemUri = $"http://out-of-band.example/{Guid.NewGuid():N}";
        (await _cache.TryGetSystemIdAsync(systemUri, CancellationToken.None)).ShouldBeNull();

        await _database.ExecuteNonQueryAsync($"INSERT INTO dbo.System (Value) VALUES ('{systemUri}')");
        var realId = await _database.ExecuteScalarAsync<int>(
            $"SELECT SystemId FROM dbo.System WHERE Value = '{systemUri}'");

        (await _cache.TryGetSystemIdAsync(systemUri, CancellationToken.None)).ShouldBeNull(
            "within its lifetime the recorded miss is still answered from memory");

        // Act
        _time.Advance(TimeSpan.FromMinutes(6));

        // Assert
        (await _cache.TryGetSystemIdAsync(systemUri, CancellationToken.None)).ShouldBe(realId);
    }

    [Fact]
    public async Task GivenAProbeThatRecordedAQuantityCodeMissing_WhenTheRowIsCreatedOutOfBandAndTheTtlElapses_ThenTheLookupReturnsItsRealId()
    {
        // Arrange
        var code = $"out-of-band-{Guid.NewGuid():N}";
        (await _cache.TryGetQuantityCodeIdAsync(code, CancellationToken.None)).ShouldBeNull();

        await _database.ExecuteNonQueryAsync($"INSERT INTO dbo.QuantityCode (Value) VALUES ('{code}')");
        var realId = await _database.ExecuteScalarAsync<int>(
            $"SELECT QuantityCodeId FROM dbo.QuantityCode WHERE Value = '{code}'");

        (await _cache.TryGetQuantityCodeIdAsync(code, CancellationToken.None)).ShouldBeNull();

        // Act
        _time.Advance(TimeSpan.FromMinutes(6));

        // Assert
        (await _cache.TryGetQuantityCodeIdAsync(code, CancellationToken.None)).ShouldBe(realId);
    }

    [Fact]
    public async Task GivenAProbeThatRecordedASystemMissing_WhenTheMissIsForgotten_ThenTheLookupReturnsItsRealIdWithoutWaitingForTheTtl()
    {
        // Arrange
        var systemUri = $"http://forgotten.example/{Guid.NewGuid():N}";
        (await _cache.TryGetSystemIdAsync(systemUri, CancellationToken.None)).ShouldBeNull();

        await _database.ExecuteNonQueryAsync($"INSERT INTO dbo.System (Value) VALUES ('{systemUri}')");
        var realId = await _database.ExecuteScalarAsync<int>(
            $"SELECT SystemId FROM dbo.System WHERE Value = '{systemUri}'");

        // Act
        _cache.ForgetMissingSystem(systemUri);

        // Assert
        (await _cache.TryGetSystemIdAsync(systemUri, CancellationToken.None)).ShouldBe(realId);
    }

    [Fact]
    public async Task GivenAProbeThatRecordedAQuantityCodeMissing_WhenTheMissIsForgotten_ThenTheLookupReturnsItsRealIdWithoutWaitingForTheTtl()
    {
        // Arrange
        var code = $"forgotten-{Guid.NewGuid():N}";
        (await _cache.TryGetQuantityCodeIdAsync(code, CancellationToken.None)).ShouldBeNull();

        await _database.ExecuteNonQueryAsync($"INSERT INTO dbo.QuantityCode (Value) VALUES ('{code}')");
        var realId = await _database.ExecuteScalarAsync<int>(
            $"SELECT QuantityCodeId FROM dbo.QuantityCode WHERE Value = '{code}'");

        // Act
        _cache.ForgetMissingQuantityCode(code);

        // Assert
        (await _cache.TryGetQuantityCodeIdAsync(code, CancellationToken.None)).ShouldBe(realId);
    }

    [Fact]
    public async Task GivenASystemRecordedMissingByTheReadPath_WhenTheWritePathCreatesIt_ThenNoSentinelReachesTheWriteMappings()
    {
        // Arrange: the read path must never leave a value in the dictionary the row generators read, or the
        // write path would treat -1 as a real SystemId and index rows against a system that does not exist.
        var systemUri = $"http://no-sentinel.example/{Guid.NewGuid():N}";
        (await _cache.TryGetSystemIdAsync(systemUri, CancellationToken.None)).ShouldBeNull();

        // Act
        _cache.SystemMappings.ContainsKey(systemUri).ShouldBeFalse(
            "a recorded miss must not appear as an entry in the positive mappings at all");
        var found = _cache.SystemMappings.TryGetValue(systemUri, out var resolvedId);

        // Assert
        found.ShouldBeTrue();
        resolvedId.ShouldBeGreaterThan(0);
        (await _cache.TryGetSystemIdAsync(systemUri, CancellationToken.None)).ShouldBe(resolvedId);
    }
}
