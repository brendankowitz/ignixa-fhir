using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.DataLayer.SqlServer.Tests.Indexing;

/// <summary>
/// Pins the regression a PR review found: every entry point below has a fast path that answers straight from
/// an in-memory dictionary (or, for the two Ensure*PreloadedAsync methods, a no-op once already loaded) --
/// and on a WARM cache, that path returned an answer without ever consulting
/// <see cref="CancellationToken.ThrowIfCancellationRequested"/>. A caller cancelled behind a warm cache never
/// learned it was cancelled, so a cancelled write kept running under load-shedding instead of unwinding.
/// <para>
/// Each test warms the relevant entry first with a non-cancelled call, then repeats the call with an
/// already-cancelled token. <c>CallCount</c> staying at the priming call's count on the second call is
/// incidental confirmation (the fixture is never touched again); the load-bearing assertion is the throw
/// itself -- without the guard, the warm path returns the cached answer instead.
/// </para>
/// </summary>
public class SqlServerSearchIndexReferenceDataCacheCancellationTests
{
    [Fact]
    public async Task GivenAWarmCacheAndACancelledToken_WhenGetResourceTypeIdAsync_ThenThrows()
    {
        // Arrange: seeded directly via the public cache-write hook, so UnusableSqlExecutionService proves
        // the warm path cannot be falling through to the database.
        using var cache = new SqlServerSearchIndexReferenceDataCache(
            new UnusableSqlExecutionService(), tenantId: 1, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        cache.CacheResourceTypeId("Patient", 1);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            () => cache.GetResourceTypeIdAsync("Patient", cts.Token));
    }

    [Fact]
    public async Task GivenAWarmCacheAndACancelledToken_WhenGetSearchParamIdAsync_ThenThrows()
    {
        // Arrange
        var sql = new FixedRowsSqlExecutionService<short>(1);
        using var cache = new SqlServerSearchIndexReferenceDataCache(
            sql, tenantId: 1, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        await cache.GetSearchParamIdAsync("http://hl7.org/fhir/SearchParameter/Patient-name", CancellationToken.None);
        sql.CallCount.ShouldBe(1);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            () => cache.GetSearchParamIdAsync("http://hl7.org/fhir/SearchParameter/Patient-name", cts.Token));
        sql.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task GivenAWarmCacheAndACancelledToken_WhenGetOrCreateSystemIdAsync_ThenThrows()
    {
        // Arrange
        var sql = new FixedRowsSqlExecutionService<int>(1);
        using var cache = new SqlServerSearchIndexReferenceDataCache(
            sql, tenantId: 1, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        await cache.GetOrCreateSystemIdAsync("http://loinc.org", CancellationToken.None);
        sql.CallCount.ShouldBe(1);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            () => cache.GetOrCreateSystemIdAsync("http://loinc.org", cts.Token));
        sql.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task GivenAWarmCacheAndACancelledToken_WhenGetOrCreateQuantityCodeIdAsync_ThenThrows()
    {
        // Arrange
        var sql = new FixedRowsSqlExecutionService<int>(1);
        using var cache = new SqlServerSearchIndexReferenceDataCache(
            sql, tenantId: 1, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        await cache.GetOrCreateQuantityCodeIdAsync("mg", CancellationToken.None);
        sql.CallCount.ShouldBe(1);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            () => cache.GetOrCreateQuantityCodeIdAsync("mg", cts.Token));
        sql.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task GivenAWarmCacheAndACancelledToken_WhenTryGetSystemIdAsync_ThenThrows()
    {
        // Arrange
        var sql = new FixedRowsSqlExecutionService<int>(1);
        using var cache = new SqlServerSearchIndexReferenceDataCache(
            sql, tenantId: 1, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        await cache.TryGetSystemIdAsync("http://loinc.org", CancellationToken.None);
        sql.CallCount.ShouldBe(1);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            () => cache.TryGetSystemIdAsync("http://loinc.org", cts.Token));
        sql.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task GivenAWarmCacheAndACancelledToken_WhenTryGetQuantityCodeIdAsync_ThenThrows()
    {
        // Arrange
        var sql = new FixedRowsSqlExecutionService<int>(1);
        using var cache = new SqlServerSearchIndexReferenceDataCache(
            sql, tenantId: 1, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        await cache.TryGetQuantityCodeIdAsync("mg", CancellationToken.None);
        sql.CallCount.ShouldBe(1);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            () => cache.TryGetQuantityCodeIdAsync("mg", cts.Token));
        sql.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task GivenResourceTypesAlreadyPreloadedAndACancelledToken_WhenEnsureResourceTypesPreloadedAsync_ThenThrows()
    {
        // Arrange: the fast path here is a no-op ("_resourceTypesLoaded" already true), not a dictionary hit --
        // it is checked before either the cache or the token, so this needs its own coverage.
        var sql = new FixedRowsSqlExecutionService<(short Id, string Name)>();
        using var cache = new SqlServerSearchIndexReferenceDataCache(
            sql, tenantId: 1, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        await cache.EnsureResourceTypesPreloadedAsync(CancellationToken.None);
        sql.CallCount.ShouldBe(1);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            () => cache.EnsureResourceTypesPreloadedAsync(cts.Token));
        sql.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task GivenSearchParametersAlreadyPreloadedAndACancelledToken_WhenEnsureSearchParametersPreloadedAsync_ThenThrows()
    {
        // Arrange -- see the resource-types test above for why the no-op path needs its own coverage.
        var sql = new FixedRowsSqlExecutionService<(short Id, string Uri)>();
        using var cache = new SqlServerSearchIndexReferenceDataCache(
            sql, tenantId: 1, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        await cache.EnsureSearchParametersPreloadedAsync(CancellationToken.None);
        sql.CallCount.ShouldBe(1);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            () => cache.EnsureSearchParametersPreloadedAsync(cts.Token));
        sql.CallCount.ShouldBe(1);
    }
}
