using System.Diagnostics;
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

public class DeadlockAllocationRecoveryTests : IAsyncLifetime
{
    private TestTenantDatabase _database = null!;

    public async Task InitializeAsync() => _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task GivenAnUncertainTransportFailure_WhenCoreWriteFails_ThenTheAllocationRemainsForReconciliation()
    {
        var failure = new IOException("The core write acknowledgement was lost.");
        var sql = new CoreFailureExecutionService(_database.SqlExecutionService, failure);
        using var cache = new SqlServerSearchIndexReferenceDataCache(
            sql, 1, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        await cache.PreloadResourceTypesAsync(CancellationToken.None);
        var compressor = new GzipResourceCompressor(new RecyclableMemoryStreamManager());
        var merge = new SqlServerMergeRepository(sql, 1, compressor, cache,
            new SqlServerPostMergeExtensionUpdater(sql, 1, NullLogger<SqlServerPostMergeExtensionUpdater>.Instance),
            NullLogger<SqlServerMergeRepository>.Instance);
        var repository = new SqlServerFhirRepository(sql, 1, compressor, cache, merge,
            NullLogger<SqlServerFhirRepository>.Instance);

        var actual = await Should.ThrowAsync<IOException>(async () =>
            await repository.CreateOrUpdateAsync(Patient("uncertain")));

        actual.ShouldBeSameAs(failure);
        (await _database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Transactions WHERE IsCompleted = 0"))
            .ShouldBe(1);
        (await _database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Transactions WHERE IsVisible = 1"))
            .ShouldBe(0);
    }

    [Fact]
    public async Task GivenADeadlockVictim_WhenTheGuardedWriteRollsBack_ThenLaterWritesCanBecomeVisible()
    {
        await _database.ExecuteNonQueryAsync("""
            CREATE TABLE dbo.ResourceDeadlockGate (Id int NOT NULL PRIMARY KEY, Value int NOT NULL);
            INSERT INTO dbo.ResourceDeadlockGate VALUES (1, 0);
            """);
        await _database.ExecuteNonQueryAsync("""
            CREATE TRIGGER dbo.DeadlockOneResource ON dbo.Resource AFTER INSERT AS
            BEGIN
              IF EXISTS (SELECT 1 FROM inserted WHERE ResourceId = 'deadlock-victim')
              BEGIN
                SET DEADLOCK_PRIORITY LOW;
                UPDATE dbo.ResourceDeadlockGate SET Value = Value + 1 WHERE Id = 1;
              END
            END
            """);

        await using var blocker = new SqlConnection(_database.ConnectionString);
        await blocker.OpenAsync();
        await using var transaction = (SqlTransaction)await blocker.BeginTransactionAsync();
        using (var gate = new SqlCommand(
            "SET DEADLOCK_PRIORITY HIGH; UPDATE dbo.ResourceDeadlockGate SET Value = Value + 1 WHERE Id = 1;",
            blocker, transaction))
        {
            await gate.ExecuteNonQueryAsync();
        }
        var write = _database.Repository.CreateOrUpdateAsync(Patient("deadlock-victim")).AsTask();
        await WaitForBlockedWriteAsync(blocker.ServerProcessId);
        using (var cycle = new SqlCommand("""
            SELECT COUNT(*) FROM dbo.Resource WITH (UPDLOCK, HOLDLOCK)
            WHERE ResourceId = 'deadlock-victim';
            """, blocker, transaction))
        {
            cycle.CommandTimeout = 30;
            await cycle.ExecuteScalarAsync();
        }
        await transaction.CommitAsync();

        var exception = await Should.ThrowAsync<SqlException>(async () => await write);
        exception.Number.ShouldBe(1205);
        (await _database.Repository.GetAsync(new ResourceKey("Patient", "deadlock-victim"))).ShouldBeNull();
        (await _database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Transactions WHERE IsCompleted = 0"))
            .ShouldBe(0);

        await _database.Repository.CreateOrUpdateAsync(Patient("after-deadlock"));
        (await _database.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.Resource r
            JOIN dbo.Transactions t ON r.TransactionId = t.SurrogateIdRangeFirstValue
            WHERE r.ResourceId = 'after-deadlock' AND t.IsVisible = 1 AND t.VisibleDate IS NOT NULL;
            """)).ShouldBe(1);
    }

    private async Task WaitForBlockedWriteAsync(int blockerSessionId)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(20))
        {
            var blocked = await _database.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM sys.dm_exec_requests WHERE database_id = DB_ID() AND blocking_session_id = {blockerSessionId};");
            if (blocked > 0)
            {
                return;
            }
            await Task.Delay(25);
        }
        throw new TimeoutException("The guarded resource write did not reach the SQL deadlock gate.");
    }

    private static ResourceWrapper Patient(string id) => new(
        "Patient", id, "1", DateTimeOffset.UtcNow,
        ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{id}}"}"""),
        new ResourceRequest("PUT", $"Patient/{id}"));

    private sealed class CoreFailureExecutionService(ISqlExecutionService inner, Exception failure) : ISqlExecutionService
    {
        public Task<int> ExecuteNonQueryAsync(int tenantId, SqlCommand command, CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent) =>
            command.CommandText.Contains("EXEC dbo.MergeResources @AffectedRows", StringComparison.Ordinal)
                ? Task.FromException<int>(failure)
                : inner.ExecuteNonQueryAsync(tenantId, command, cancellationToken, idempotency);

        public Task<IReadOnlyList<T>> ExecuteReaderAsync<T>(int tenantId, SqlCommand command,
            Func<SqlDataReader, T> readRow, CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent) =>
            inner.ExecuteReaderAsync(tenantId, command, readRow, cancellationToken, idempotency);

        public Task<T> ExecuteInTransactionAsync<T>(int tenantId,
            Func<ISqlTransactionContext, CancellationToken, Task<T>> work, CancellationToken cancellationToken) =>
            inner.ExecuteInTransactionAsync(tenantId, work, cancellationToken);

        public Task ExecuteInTransactionAsync(int tenantId,
            Func<ISqlTransactionContext, CancellationToken, Task> work, CancellationToken cancellationToken) =>
            inner.ExecuteInTransactionAsync(tenantId, work, cancellationToken);
    }
}
