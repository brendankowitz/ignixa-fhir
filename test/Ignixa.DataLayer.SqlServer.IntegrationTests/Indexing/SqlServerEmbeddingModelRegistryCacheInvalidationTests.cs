using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.DataLayer.SqlServer.SemanticSearch;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Indexing;

/// <summary>
/// <see cref="SqlServerEmbeddingModelRegistry"/> caches <c>ModelKey -&gt; EmbeddingModelId</c> on the
/// tenant-scoped <c>SqlServerSearchIndexReferenceDataCache</c> instance <c>SqlServerSearchIndexCacheRegistry</c>
/// owns for that tenant, specifically so <c>Invalidate(tenantId)</c> clears it along with everything else
/// the cache holds. These tests pin that: a mapping resolved before invalidation must not survive it, and a
/// re-resolution afterward must reach the database rather than answer from a stale, process-lifetime cache
/// (the bug this design replaces -- see the type's remarks).
/// </summary>
public class SqlServerEmbeddingModelRegistryCacheInvalidationTests : IAsyncLifetime
{
    private const string ModelKey = "cache-invalidation-model|v1";

    private TerminologyTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await TerminologyTestFixture.CreateAsync();

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task GivenACachedEmbeddingModelId_WhenTheTenantCacheIsInvalidated_ThenAReResolutionHitsTheDatabase()
    {
        var cache = await _fixture.CacheRegistry.GetOrCreateAsync(TestTenantDatabase.TestTenantId, CancellationToken.None);
        var registry = new SqlServerEmbeddingModelRegistry(
            _fixture.SqlExecutionService, TestTenantDatabase.TestTenantId, cache, NullLogger<SqlServerEmbeddingModelRegistry>.Instance);

        var firstId = await registry.GetIdAsync(ModelKey, 1536, CancellationToken.None);

        (await _fixture.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.EmbeddingModel WHERE ModelKey = '{ModelKey}' AND EmbeddingModelId = {firstId}"))
            .ShouldBe(1, "without a seeded row, this test cannot prove the row it deletes next ever existed");

        // Simulates the tenant's database being dropped and re-provisioned under the same TenantId: the
        // row GetOrCreateEmbeddingModel created is gone, but nothing told the in-process cache.
        await _fixture.ExecuteNonQueryAsync($"DELETE FROM dbo.EmbeddingModel WHERE EmbeddingModelId = {firstId}");

        // Without invalidation, the cache still answers with the now-dangling id -- a cache is a cache; it
        // does not poll the database on every call. This is the defect's exact shape: the id below belongs
        // to no row in dbo.EmbeddingModel at all.
        (await registry.GetIdAsync(ModelKey, 1536, CancellationToken.None)).ShouldBe(
            firstId, "a cache that re-validated against the database on every hit would not need invalidation at all");

        _fixture.CacheRegistry.Invalidate(TestTenantDatabase.TestTenantId).ShouldBeTrue();

        var freshCache = await _fixture.CacheRegistry.GetOrCreateAsync(TestTenantDatabase.TestTenantId, CancellationToken.None);
        freshCache.ShouldNotBeSameAs(cache, "invalidation must replace the instance, not merely clear it in place");
        var freshRegistry = new SqlServerEmbeddingModelRegistry(
            _fixture.SqlExecutionService, TestTenantDatabase.TestTenantId, freshCache, NullLogger<SqlServerEmbeddingModelRegistry>.Instance);

        var secondId = await freshRegistry.GetIdAsync(ModelKey, 1536, CancellationToken.None);

        // A second value proves this call reached dbo.GetOrCreateEmbeddingModel rather than returning the
        // stale, now-dangling id: the deleted row's identity value is never reused, so re-creating it under
        // the same ModelKey necessarily advances to a new one.
        secondId.ShouldNotBe(firstId, "a re-resolution after invalidation must hit the database, not the stale cache");
        (await _fixture.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.EmbeddingModel WHERE ModelKey = '{ModelKey}' AND EmbeddingModelId = {secondId}"))
            .ShouldBe(1);
    }
}
