using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

// CA1001 (owns a disposable field but isn't itself IDisposable): mirrors
// SqlServerSearchIndexReferenceDataCacheTests.cs's own suppression rationale -- the cache's only
// disposable is a SemaphoreSlim, explicitly disposed in DisposeAsync below, and xunit already
// drives this type's lifecycle through IAsyncLifetime.
#pragma warning disable CA1001
public class SqlServerSearchIndexReferenceDataCacheReadOnlyLookupTests : IAsyncLifetime
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
    public async Task GivenASystemNeverInserted_WhenLookedUpReadOnly_ThenReturnsNullAndDoesNotInsertARow()
    {
        // Arrange
        const string unknownSystem = "http://never-inserted.example.org/this-specific-test-run";

        // Act
        var id = await _cache.TryGetSystemIdAsync(unknownSystem, CancellationToken.None);

        // Assert
        id.ShouldBeNull();

        // Assert no row was created as a side effect (this is the whole point -- get-or-create semantics
        // would have inserted one)
        var idAfterASecondLookup = await _cache.TryGetSystemIdAsync(unknownSystem, CancellationToken.None);
        idAfterASecondLookup.ShouldBeNull();

        var rowCount = await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.System WHERE Value = '{unknownSystem}'");
        rowCount.ShouldBe(0);
    }

    [Fact]
    public async Task GivenASystemAlreadyInsertedByTheWritePath_WhenLookedUpReadOnly_ThenReturnsItsRealId()
    {
        // Arrange
        const string knownSystem = "http://real-write-path-system.example.org/for-this-test";
        var insertedId = await _cache.GetOrCreateSystemIdAsync(knownSystem, CancellationToken.None);

        // Act -- a FRESH cache instance, to prove this reads from the database, not the same
        // in-process dictionary the insert just warmed
        using var freshCache = new SqlServerSearchIndexReferenceDataCache(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        var readId = await freshCache.TryGetSystemIdAsync(knownSystem, CancellationToken.None);

        // Assert
        readId.ShouldBe(insertedId);
    }

    [Fact]
    public async Task GivenAQuantityCodeNeverInserted_WhenLookedUpReadOnly_ThenReturnsNullAndDoesNotInsertARow()
    {
        // Arrange
        var unknownCode = $"never-inserted-code-{Guid.NewGuid():N}";

        // Act
        var id = await _cache.TryGetQuantityCodeIdAsync(unknownCode, CancellationToken.None);

        // Assert
        id.ShouldBeNull();

        var rowCount = await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.QuantityCode WHERE Value = '{unknownCode}'");
        rowCount.ShouldBe(0);
    }

    [Fact]
    public async Task GivenASystemMissedByReadOnlyLookup_WhenLaterCreatedByTheWritePath_ThenReturnsTheRealIdNotTheStaleSentinel()
    {
        // Arrange -- ONE shared instance across both calls: this specifically exercises the
        // shared-dictionary sentinel interaction, not two fresh instances
        var system = $"http://later-created-system.example.org/{Guid.NewGuid():N}";
        var missedId = await _cache.TryGetSystemIdAsync(system, CancellationToken.None);
        missedId.ShouldBeNull();

        // Act
        var createdId = await _cache.GetOrCreateSystemIdAsync(system, CancellationToken.None);

        // Assert
        createdId.ShouldBeGreaterThan(0);
        var readBackId = await _cache.TryGetSystemIdAsync(system, CancellationToken.None);
        readBackId.ShouldBe(createdId);
    }

    [Fact]
    public async Task GivenAQuantityCodeMissedByReadOnlyLookup_WhenLaterCreatedByTheWritePath_ThenReturnsTheRealIdNotTheStaleSentinel()
    {
        // Arrange
        var code = $"later-created-code-{Guid.NewGuid():N}";
        var missedId = await _cache.TryGetQuantityCodeIdAsync(code, CancellationToken.None);
        missedId.ShouldBeNull();

        // Act
        var createdId = await _cache.GetOrCreateQuantityCodeIdAsync(code, CancellationToken.None);

        // Assert
        createdId.ShouldBeGreaterThan(0);
        var readBackId = await _cache.TryGetQuantityCodeIdAsync(code, CancellationToken.None);
        readBackId.ShouldBe(createdId);
    }

    [Fact]
    public async Task GivenASystemMissedByReadOnlyLookup_WhenTheWritePathLaterCreatesItThroughSystemMappings_ThenTheRealIdIsReturnedNotTheStaleSentinel()
    {
        // Arrange
        var system = $"http://later-created-via-systemmappings.example.org/{Guid.NewGuid():N}";
        var missedId = await _cache.TryGetSystemIdAsync(system, CancellationToken.None);
        missedId.ShouldBeNull();

        // Act -- this is the actual call shape SqlServerMergeRepository's row generators use, not
        // GetOrCreateSystemIdAsync directly
        var found = _cache.SystemMappings.TryGetValue(system, out var resolvedId);

        // Assert
        found.ShouldBeTrue();
        resolvedId.ShouldBeGreaterThan(0);

        var readBackId = await _cache.TryGetSystemIdAsync(system, CancellationToken.None);
        readBackId.ShouldBe(resolvedId);
    }

    [Fact]
    public async Task GivenAQuantityCodeMissedByReadOnlyLookup_WhenTheWritePathLaterCreatesItThroughQuantityCodeMappings_ThenTheRealIdIsReturnedNotTheStaleSentinel()
    {
        // Arrange
        var code = $"later-created-via-quantitycodemappings-{Guid.NewGuid():N}";
        var missedId = await _cache.TryGetQuantityCodeIdAsync(code, CancellationToken.None);
        missedId.ShouldBeNull();

        // Act
        var found = _cache.QuantityCodeMappings.TryGetValue(code, out var resolvedId);

        // Assert
        found.ShouldBeTrue();
        resolvedId.ShouldBeGreaterThan(0);

        var readBackId = await _cache.TryGetQuantityCodeIdAsync(code, CancellationToken.None);
        readBackId.ShouldBe(resolvedId);
    }
}
