using System.Data;
using System.Diagnostics;
using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.TtlCleanup.Activities;
using Ignixa.Application.BackgroundOperations.TtlCleanup.Models;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public sealed class TtlCleanupLifecycleTests : IAsyncLifetime, IDisposable
{
    private const string ResourceId = "ttl-lifecycle";
    private readonly DateTimeOffset _expiredAt = DateTimeOffset.UtcNow.AddDays(-1);
    private TestTenantDatabase _database = null!;
    private SqlServerSearchIndexReferenceDataCache? _cache;
    private CleanupSqlInterceptor _interceptor = null!;
    private CleanupActivity _activity = null!;
    private readonly RecordingAuditLogger _audit = new();

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();
        await SearchIndexTableSeeder.SeedSearchParameterCatalogAsync(_database, CancellationToken.None);
        await _database.Repository.CreateOrUpdateAsync(new ResourceWrapper(
            "Patient", "ttl-target", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"ttl-target"}"""),
            new ResourceRequest("PUT", "Patient/ttl-target")));
        await WriteAsync(_expiredAt);
        await WriteAsync(_expiredAt);

        _interceptor = new CleanupSqlInterceptor(_database.SqlExecutionService);
        _cache = new SqlServerSearchIndexReferenceDataCache(
            _interceptor, _database.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        var compressor = new GzipResourceCompressor(new RecyclableMemoryStreamManager());
        var merge = new SqlServerMergeRepository(
            _interceptor, _database.TenantId, compressor, _cache,
            new SqlServerPostMergeExtensionUpdater(
                _interceptor, _database.TenantId, NullLogger<SqlServerPostMergeExtensionUpdater>.Instance),
            NullLogger<SqlServerMergeRepository>.Instance);
        var repository = new SqlServerFhirRepository(
            _interceptor, _database.TenantId, compressor, _cache, merge,
            NullLogger<SqlServerFhirRepository>.Instance);
        _activity = new CleanupActivity(new RepositoryFactory(repository), _audit);
    }

    public async Task DisposeAsync()
    {
        Dispose();
        await _database.DisposeAsync();
    }

    public void Dispose()
    {
        _cache?.Dispose();
        _cache = null;
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("renew")]
    [InlineData("clear")]
    [InlineData("same-expiry")]
    [InlineData("recreate")]
    public async Task GivenExpiredSelection_WhenPutChangesTheSelectedVersion_ThenCleanupPreservesItsHistoryAndIndexes(string change)
    {
        DateTimeOffset? expiry = change == "clear" ? null : change == "renew" ? _expiredAt.AddDays(30) : _expiredAt;
        _interceptor.AfterSelection = async () =>
        {
            if (change == "recreate")
            {
                var typeId = await _database.ExecuteScalarAsync<short>(
                    "SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name = 'Patient'");
                await _database.Repository.HardDeleteResourceAsync(typeId, ResourceId);
            }
            await WriteAsync(expiry);
        };

        var result = await _activity.RunAsync();

        result.ExpiredCount.ShouldBe(1);
        result.FailedCount.ShouldBe(0);
        result.DeletedCount.ShouldBe(0);
        _audit.Deletions.ShouldBeEmpty();
        await AssertIntactAsync(change == "recreate" ? 1 : 3, expiry);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenExpiredSelection_WhenExpiryChangesWithoutReplacingTheVersion_ThenCleanupRechecksTheCurrentExpiry(bool clear)
    {
        _interceptor.AfterSelection = () => _database.ExecuteNonQueryAsync(clear
            ? "DELETE dbo.ResourceTtl WHERE ResourceId = 'ttl-lifecycle'"
            : "UPDATE dbo.ResourceTtl SET ExpiresAt = DATEADD(day, 30, ExpiresAt) WHERE ResourceId = 'ttl-lifecycle'");

        var result = await _activity.RunAsync();

        result.DeletedCount.ShouldBe(0);
        result.FailedCount.ShouldBe(0);
        _audit.Deletions.ShouldBeEmpty();
        await AssertIntactAsync(2, clear ? null : _expiredAt.AddDays(30));
    }

    [Fact]
    public async Task GivenExpiredSelection_WhenRenewalCommitsAsDeletionTransactionStarts_ThenTheLockedRecheckSkipsIt()
    {
        _interceptor.BeforeTransaction = async () => await WriteAsync(null);

        var result = await _activity.RunAsync();

        result.DeletedCount.ShouldBe(0);
        result.FailedCount.ShouldBe(0);
        _audit.Deletions.ShouldBeEmpty();
        await AssertIntactAsync(3, null);
    }

    [Fact]
    public async Task GivenTrulyExpiredResource_WhenCleanupRuns_ThenAllVersionsIndexesAndTtlAreRemovedAndAudited()
    {
        var surrogateId = await CurrentSurrogateIdAsync();
        await SearchIndexTableSeeder.InsertResourceWriteClaimAsync(_database, surrogateId, CancellationToken.None);
        await SearchIndexTableSeeder.AssertEverySearchIndexTableHasRowsAsync(_database, surrogateId, CancellationToken.None);

        var result = await _activity.RunAsync();

        result.DeletedCount.ShouldBe(1);
        result.FailedCount.ShouldBe(0);
        _audit.Deletions.ShouldBe([true]);
        await AssertDeletedAsync(surrogateId);
    }

    [Fact]
    public async Task GivenCleanupHoldingItsExpiryLocks_WhenPutRenewsConcurrently_ThenItWaitsAndCannotCommitAPartialResource()
    {
        var surrogateId = await CurrentSurrogateIdAsync();
        Task<UpdateResult>? renewal = null;
        _interceptor.AfterExpiryLock = async () =>
        {
            renewal = WriteAsync(_expiredAt.AddDays(30));
            await WaitForBlockedRenewalAsync(renewal);
        };

        var result = await _activity.RunAsync();

        _ = renewal.ShouldNotBeNull("the actual in-transaction expiry recheck must execute");
        result.DeletedCount.ShouldBe(1);
        result.FailedCount.ShouldBe(0);
        await Should.ThrowAsync<PreconditionFailedException>(async () => await renewal!.WaitAsync(TimeSpan.FromSeconds(30)));
        await AssertDeletedAsync(surrogateId);

        await WriteAsync(null);
        await AssertIntactAsync(1, null);
    }

    [Fact]
    public async Task GivenAnUnexpiredResource_WhenExplicitlyHardDeleted_ThenManualErasureIsNotConditionalOnTtl()
    {
        await WriteAsync(_expiredAt.AddDays(30));
        var surrogateId = await CurrentSurrogateIdAsync();
        var typeId = await _database.ExecuteScalarAsync<short>(
            "SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name = 'Patient'");

        await _database.Repository.HardDeleteResourceAsync(typeId, ResourceId);

        await AssertDeletedAsync(surrogateId);
    }

    private Task<UpdateResult> WriteAsync(DateTimeOffset? expiry) =>
        _database.Repository.CreateOrUpdateAsync(new ResourceWrapper(
            "Patient", ResourceId, "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{ResourceId}}"}"""),
            new ResourceRequest("PUT", $"Patient/{ResourceId}"))
        {
            ExpiresAt = expiry,
            SearchIndices = SearchIndexTableSeeder.BuildSearchIndicesCoveringEverySearchIndexTable("ttl-target")
        }).AsTask();

    private Task<long> CurrentSurrogateIdAsync() => _database.ExecuteScalarAsync<long>(
        "SELECT ResourceSurrogateId FROM dbo.Resource WHERE ResourceId = 'ttl-lifecycle' AND IsHistory = 0");

    private async Task AssertIntactAsync(int versions, DateTimeOffset? expiry)
    {
        var current = await _database.Repository.GetAsync(new ResourceKey("Patient", ResourceId));
        current.ShouldNotBeNull();
        current.VersionId.ShouldBe(versions.ToString());
        (await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = 'ttl-lifecycle'")).ShouldBe(versions);
        List<SearchEntryResult> history = [];
        await foreach (var row in _database.Repository.GetResourceHistoryAsync(
            new ResourceKey("Patient", ResourceId), new HistoryQueryParameters()))
        {
            history.Add(row);
        }
        history.Count.ShouldBe(versions);
        var sid = await CurrentSurrogateIdAsync();
        foreach (var table in SearchIndexTableSeeder.SearchIndexTables.Where(t => t != "ResourceWriteClaim"))
        {
            (await _database.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM dbo.{table} WHERE ResourceSurrogateId = {sid}")).ShouldBeGreaterThan(0, table);
        }
        (await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ResourceTtl WHERE ResourceId = 'ttl-lifecycle'")).ShouldBe(expiry.HasValue ? 1 : 0);
        if (expiry.HasValue)
        {
            await using var connection = new SqlConnection(_database.ConnectionString);
            await connection.OpenAsync();
            using var command = new SqlCommand(
                "SELECT ExpiresAt FROM dbo.ResourceTtl WHERE ResourceId = 'ttl-lifecycle'", connection);
            ((DateTimeOffset)(await command.ExecuteScalarAsync())!).ShouldBe(expiry.Value);
        }
    }

    private async Task AssertDeletedAsync(long sid)
    {
        (await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = 'ttl-lifecycle'")).ShouldBe(0);
        (await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ResourceTtl WHERE ResourceId = 'ttl-lifecycle'")).ShouldBe(0);
        foreach (var table in SearchIndexTableSeeder.SearchIndexTables)
        {
            (await _database.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM dbo.{table} WHERE ResourceSurrogateId = {sid}")).ShouldBe(0, table);
        }
    }

    private async Task WaitForBlockedRenewalAsync(Task renewal)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(15))
        {
            var blocked = await _database.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM sys.dm_exec_requests
                WHERE database_id = DB_ID() AND blocking_session_id > 0 AND wait_type LIKE 'LCK_M_%'
                """);
            if (blocked > 0)
            {
                renewal.IsCompleted.ShouldBeFalse();
                return;
            }
            renewal.IsCompleted.ShouldBeFalse("renewal must wait for cleanup's current-resource lock");
            await Task.Delay(25);
        }
        throw new TimeoutException("The concurrent renewal did not reach the cleanup's SQL lock.");
    }

    private sealed class RepositoryFactory(IFhirRepository repository) : IFhirRepositoryFactory
    {
        public Task<IFhirRepository> GetRepositoryAsync(int tenantId, CancellationToken ct = default) =>
            Task.FromResult(repository);
    }

    private sealed class CleanupActivity(IFhirRepositoryFactory factory, IAuditLogger audit)
        : TtlCleanupActivity(factory, audit, NullLogger<TtlCleanupActivity>.Instance)
    {
        public Task<TtlCleanupActivityOutput> RunAsync() =>
            ExecuteAsync(null!, new TtlCleanupActivityInput(TestTenantDatabase.TestTenantId, 10));
    }

    private sealed class RecordingAuditLogger : IAuditLogger
    {
        public List<bool> Deletions { get; } = [];
        public void LogTenantAccess(string userId, int tenantId, string operation, string resourceType, string? resourceId, bool authorized) { }
        public void LogHttpRequest(HttpRequestAuditEvent auditEvent) { }
        public void LogTtlDeletion(int tenantId, string resourceType, string resourceId, DateTimeOffset expiresAt, bool success) =>
            Deletions.Add(success);
    }

    private sealed class CleanupSqlInterceptor(ISqlExecutionService inner) : ISqlExecutionService
    {
        public Func<Task>? AfterSelection { get; set; }
        public Func<Task>? BeforeTransaction { get; set; }
        public Func<Task>? AfterExpiryLock { get; set; }

        public async Task<IReadOnlyList<T>> ExecuteReaderAsync<T>(
            int tenantId, SqlCommand command, Func<SqlDataReader, T> readRow,
            CancellationToken cancellationToken, SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
        {
            var rows = await inner.ExecuteReaderAsync(tenantId, command, readRow, cancellationToken, idempotency);
            if (command.CommandText.Contains("FROM dbo.ResourceTtl t", StringComparison.Ordinal) && AfterSelection is { } hook)
            {
                AfterSelection = null;
                await hook();
            }
            return rows;
        }

        public Task<int> ExecuteNonQueryAsync(
            int tenantId, SqlCommand command, CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent) =>
            inner.ExecuteNonQueryAsync(tenantId, command, cancellationToken, idempotency);

        public async Task<T> ExecuteInTransactionAsync<T>(
            int tenantId, Func<ISqlTransactionContext, CancellationToken, Task<T>> work, CancellationToken cancellationToken)
        {
            if (BeforeTransaction is { } hook)
            {
                BeforeTransaction = null;
                await hook();
            }
            return await inner.ExecuteInTransactionAsync(
                tenantId, (context, ct) => work(new TransactionContext(this, context), ct), cancellationToken);
        }

        public async Task ExecuteInTransactionAsync(
            int tenantId, Func<ISqlTransactionContext, CancellationToken, Task> work, CancellationToken cancellationToken) =>
            await ExecuteInTransactionAsync<object?>(tenantId, async (context, ct) =>
            {
                await work(context, ct);
                return null;
            }, cancellationToken);

        private sealed class TransactionContext(CleanupSqlInterceptor owner, ISqlTransactionContext inner) : ISqlTransactionContext
        {
            public Task<int> ExecuteNonQueryAsync(SqlCommand command, CancellationToken cancellationToken) =>
                inner.ExecuteNonQueryAsync(command, cancellationToken);

            public async Task<IReadOnlyList<T>> ExecuteReaderAsync<T>(
                SqlCommand command, Func<SqlDataReader, T> readRow, CancellationToken cancellationToken)
            {
                var rows = await inner.ExecuteReaderAsync(command, readRow, cancellationToken);
                if (command.CommandText.Contains("@ExpectedExpiry", StringComparison.Ordinal) && owner.AfterExpiryLock is { } hook)
                {
                    owner.AfterExpiryLock = null;
                    await hook();
                }
                return rows;
            }
        }
    }
}
