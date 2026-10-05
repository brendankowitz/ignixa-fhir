using System.Collections.Concurrent;
using System.Data;
using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class ResourceTypeCreationConcurrencyTests : IAsyncLifetime
{
    private TestTenantDatabase _database = null!;
    private readonly List<SqlServerSearchIndexReferenceDataCache> _caches = [];

    public async Task InitializeAsync() => _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();

    public async Task DisposeAsync()
    {
        foreach (var cache in _caches)
        {
            cache.Dispose();
        }
        await _database.DisposeAsync();
    }

    [Theory]
    [InlineData("Encounter", true)]
    [InlineData("Encounter", false)]
    [InlineData("Provenance", false)]
    public async Task GivenTwoCreatorsThatBothObservedAMissingType_WhenTheyWrite_ThenTheyShareOneCanonicalId(
        string resourceType, bool shareCache)
    {
        var gate = new CreationGate(2);
        var firstSql = new CatalogExecutionObserver(_database.SqlExecutionService) { BeforeCreate = gate.WaitAsync };
        var secondSql = new CatalogExecutionObserver(_database.SqlExecutionService) { BeforeCreate = gate.WaitAsync };
        var firstCache = await CreateCacheAsync(firstSql);
        var secondCache = shareCache ? firstCache : await CreateCacheAsync(secondSql);
        var first = CreateRepository(firstSql, firstCache);
        var second = CreateRepository(secondSql, secondCache);

        var firstWrite = first.CreateOrUpdateAsync(Resource(resourceType, "first-writer")).AsTask();
        var secondWrite = second.CreateOrUpdateAsync(Resource(resourceType, "second-writer")).AsTask();
        try
        {
            await gate.AllArrived.Task.WaitAsync(TimeSpan.FromSeconds(20));
            firstSql.CreationAttempts.ShouldBe(1);
            secondSql.CreationAttempts.ShouldBe(1);
        }
        finally
        {
            gate.Release.TrySetResult();
        }

        var results = await Task.WhenAll(firstWrite, secondWrite);

        results.ShouldAllBe(result => result.Key.VersionId == "1");
        var canonicalId = await ReadTypeIdAsync(resourceType);
        firstCache.TryGetResourceTypeIdFromCache(resourceType).ShouldBe(canonicalId);
        secondCache.TryGetResourceTypeIdFromCache(resourceType).ShouldBe(canonicalId);
        (await _database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Resource")).ShouldBe(2);
        (await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.Resource WHERE ResourceTypeId = {canonicalId}")).ShouldBe(2);
        firstSql.CreationPolicies.ShouldAllBe(policy => policy == SqlCommandIdempotency.NonIdempotent);
        secondSql.CreationPolicies.ShouldAllBe(policy => policy == SqlCommandIdempotency.NonIdempotent);
    }

    [Fact]
    public async Task GivenAnExternalCreatorWinsAfterTheLookup_WhenTheRepositoryContinues_ThenItUsesTheExistingId()
    {
        const string ResourceType = "Provenance";
        var gate = new CreationGate(1);
        var sql = new CatalogExecutionObserver(_database.SqlExecutionService) { BeforeCreate = gate.WaitAsync };
        var cache = await CreateCacheAsync(sql);
        var repository = CreateRepository(sql, cache);
        var write = repository.CreateOrUpdateAsync(Resource(ResourceType, "external-winner")).AsTask();
        short externalId;
        try
        {
            await gate.AllArrived.Task.WaitAsync(TimeSpan.FromSeconds(20));
            // This connection bypasses every repository instance, cache and in-process lock.
            await using var external = new SqlConnection(_database.ConnectionString);
            await external.OpenAsync();
            using var insert = new SqlCommand(
                "INSERT INTO dbo.ResourceType (Name) OUTPUT INSERTED.ResourceTypeId VALUES (@Name)", external);
            insert.Parameters.Add("@Name", SqlDbType.NVarChar).Value = ResourceType;
            externalId = (short)(await insert.ExecuteScalarAsync())!;
        }
        finally
        {
            gate.Release.TrySetResult();
        }

        var result = await write;

        result.Key.VersionId.ShouldBe("1");
        (await ReadTypeIdAsync(ResourceType)).ShouldBe(externalId);
        cache.TryGetResourceTypeIdFromCache(ResourceType).ShouldBe(externalId);
        (await repository.GetAsync(new ResourceKey(ResourceType, "external-winner"))).ShouldNotBeNull();
        sql.CreationAttempts.ShouldBe(1);
    }

    [Fact]
    public async Task GivenACommittedCatalogWriteLosesItsResponse_WhenRetriedByTheCaller_ThenItReadsTheIdWithoutReplayingCreation()
    {
        const string ResourceType = "Encounter";
        var lostResponse = new IOException("Catalog creation committed, but its acknowledgement was lost.");
        var sql = new CatalogExecutionObserver(_database.SqlExecutionService) { FailureAfterCreate = lostResponse };
        var cache = await CreateCacheAsync(sql);
        var repository = CreateRepository(sql, cache);

        var failure = await Should.ThrowAsync<IOException>(async () =>
            await repository.CreateOrUpdateAsync(Resource(ResourceType, "unacknowledged")));

        failure.ShouldBeSameAs(lostResponse);
        sql.CreationAttempts.ShouldBe(1);
        sql.CreationPolicies.Count.ShouldBe(1);
        sql.CreationPolicies.Single().ShouldBe(SqlCommandIdempotency.NonIdempotent);
        cache.TryGetResourceTypeIdFromCache(ResourceType).ShouldBe((short?)null);
        var committedId = await ReadTypeIdAsync(ResourceType);
        (await _database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Resource")).ShouldBe(0);

        sql.FailureAfterCreate = null;
        var retry = await repository.CreateOrUpdateAsync(Resource(ResourceType, "caller-retry"));

        retry.Key.VersionId.ShouldBe("1");
        sql.CreationAttempts.ShouldBe(1);
        cache.TryGetResourceTypeIdFromCache(ResourceType).ShouldBe(committedId);
        (await ReadTypeIdAsync(ResourceType)).ShouldBe(committedId);
    }

    private async Task<short> ReadTypeIdAsync(string resourceType)
    {
        using var command = new SqlCommand("SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name = @Name");
        command.Parameters.Add("@Name", SqlDbType.NVarChar).Value = resourceType;
        var rows = await _database.SqlExecutionService.ExecuteReaderAsync(
            1, command, reader => reader.GetInt16(0), CancellationToken.None);
        rows.Count.ShouldBe(1);
        return rows[0];
    }

    private async Task<SqlServerSearchIndexReferenceDataCache> CreateCacheAsync(ISqlExecutionService sql)
    {
        var cache = new SqlServerSearchIndexReferenceDataCache(
            sql, 1, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        _caches.Add(cache);
        await cache.PreloadResourceTypesAsync(CancellationToken.None);
        return cache;
    }

    private static SqlServerFhirRepository CreateRepository(
        ISqlExecutionService sql, SqlServerSearchIndexReferenceDataCache cache)
    {
        var compressor = new GzipResourceCompressor(new RecyclableMemoryStreamManager());
        var merge = new SqlServerMergeRepository(sql, 1, compressor, cache,
            new SqlServerPostMergeExtensionUpdater(sql, 1, NullLogger<SqlServerPostMergeExtensionUpdater>.Instance),
            NullLogger<SqlServerMergeRepository>.Instance);
        return new SqlServerFhirRepository(sql, 1, compressor, cache, merge,
            NullLogger<SqlServerFhirRepository>.Instance);
    }

    private static ResourceWrapper Resource(string resourceType, string id) => new(
        resourceType, id, "1", DateTimeOffset.UtcNow,
        ResourceJsonNode.Parse($$"""{"resourceType":"{{resourceType}}","id":"{{id}}"}"""),
        new ResourceRequest("PUT", $"{resourceType}/{id}"));

    private sealed class CreationGate(int expected)
    {
        private int _arrived;
        public TaskCompletionSource AllArrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrived) == expected)
            {
                AllArrived.TrySetResult();
            }
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class CatalogExecutionObserver(ISqlExecutionService inner) : ISqlExecutionService
    {
        private int _creationAttempts;
        public int CreationAttempts => Volatile.Read(ref _creationAttempts);
        public ConcurrentQueue<SqlCommandIdempotency> CreationPolicies { get; } = new();
        public Func<CancellationToken, Task>? BeforeCreate { get; init; }
        public Exception? FailureAfterCreate { get; set; }

        public async Task<IReadOnlyList<T>> ExecuteReaderAsync<T>(int tenantId, SqlCommand command,
            Func<SqlDataReader, T> readRow, CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
        {
            var creation = command.CommandText.Contains("INSERT INTO dbo.ResourceType", StringComparison.Ordinal);
            if (creation)
            {
                Interlocked.Increment(ref _creationAttempts);
                CreationPolicies.Enqueue(idempotency);
                if (BeforeCreate != null)
                {
                    await BeforeCreate(cancellationToken);
                }
            }
            var result = await inner.ExecuteReaderAsync(tenantId, command, readRow, cancellationToken, idempotency);
            if (creation && FailureAfterCreate is { } failure)
            {
                throw failure;
            }
            return result;
        }

        public Task<int> ExecuteNonQueryAsync(int tenantId, SqlCommand command, CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent) =>
            inner.ExecuteNonQueryAsync(tenantId, command, cancellationToken, idempotency);

        public Task<T> ExecuteInTransactionAsync<T>(int tenantId,
            Func<ISqlTransactionContext, CancellationToken, Task<T>> work, CancellationToken cancellationToken) =>
            inner.ExecuteInTransactionAsync(tenantId, work, cancellationToken);

        public Task ExecuteInTransactionAsync(int tenantId,
            Func<ISqlTransactionContext, CancellationToken, Task> work, CancellationToken cancellationToken) =>
            inner.ExecuteInTransactionAsync(tenantId, work, cancellationToken);
    }
}
