using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Events;
using Ignixa.DataLayer.SqlServer.EventStore;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit.Abstractions;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class SqlServerSourceEventStoreConcurrencyTests(
    SqlPersistenceContractFixture fixture,
    ITestOutputHelper output) : IClassFixture<SqlPersistenceContractFixture>
{
    private TestTenantDatabase Database => fixture.Database;

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task GivenAnAppendPausedBeforeCommit_WhenAnotherAppendAndWatermarkReaderRun_ThenNoEventsAreLost(
        bool readCommittedSnapshot, bool pauseAfterAllInserts)
    {
        await ConfigureIsolationAsync(readCommittedSnapshot);
        await Database.ExecuteNonQueryAsync("DELETE FROM dbo.SourceEvents");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var cancellationToken = timeout.Token;
        var pausedSql = new PausingExecutionService(Database.SqlExecutionService, pauseAfterAllInserts);
        var firstStore = CreateStore(pausedSql);
        var secondStore = CreateStore(Database.SqlExecutionService);
        var readerStore = CreateStore(Database.SqlExecutionService);
        var previous = await ReadFromAsync(readerStore, 0, cancellationToken);
        var watermark = previous.Count == 0 ? 0 : previous[^1].EventId;
        var stream = Guid.NewGuid().ToString();

        // Larger than one bounded command, but below the original implementation's parameter limit,
        // so this same public-contract test also runs against the exact original source.
        const int FirstAppendCount = 650;
        var firstEvents = Enumerable.Range(0, FirstAppendCount)
            .Select(i => Event(stream, $"first-{i}"))
            .ToArray();
        var firstAppend = firstStore.AppendAsync(firstEvents, cancellationToken);
        Task<IReadOnlyList<SourceEvent>>? secondAppend = null;
        Task<List<SourceEvent>>? firstRead = null;
        try
        {
            await pausedSql.Paused.Task.WaitAsync(cancellationToken);
            output.WriteLine($"Paused after {pausedSql.InsertedCount} allocated EventIds.");

            secondAppend = secondStore.AppendAsync([Event(stream, "second")], cancellationToken);
            var secondIsBlocked = await WaitForCompletionOrBlockingAsync(secondAppend, 1, cancellationToken);
            output.WriteLine($"Second append blocked by earlier uncommitted append: {secondIsBlocked}.");

            firstRead = ReadFromAsync(readerStore, watermark, cancellationToken);
            await WaitForCompletionOrBlockingAsync(firstRead, secondIsBlocked ? 2 : 1, cancellationToken);

            pausedSql.Release.TrySetResult();
            await Task.WhenAll(firstAppend, secondAppend);

            var consumed = await firstRead;
            if (consumed.Count > 0)
            {
                watermark = consumed[^1].EventId;
            }

            consumed.AddRange(await ReadFromAsync(readerStore, watermark, cancellationToken));
            var committedIds = (await firstAppend).Concat(await secondAppend)
                .Select(e => e.EventId).OrderBy(id => id).ToArray();

            consumed.Count.ShouldBe(FirstAppendCount + 1,
                "advancing ReadFrom's watermark must not skip an earlier uncommitted append");
            consumed.Select(e => e.EventId).ShouldBe(committedIds);
            consumed.Select(e => ((PackageDeactivated)e.Data).PackageId)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ShouldBe(firstEvents.Select(e => ((PackageDeactivated)e.Data).PackageId)
                    .Append("second").OrderBy(id => id, StringComparer.Ordinal));
        }
        finally
        {
            pausedSql.Release.TrySetResult();
            await firstAppend;
            if (secondAppend is not null)
            {
                await secondAppend;
            }

            if (firstRead is not null)
            {
                await firstRead;
            }
        }
    }

    private async Task ConfigureIsolationAsync(bool enabled)
    {
        var builder = new SqlConnectionStringBuilder(Database.ConnectionString);
        var databaseName = builder.InitialCatalog;
        databaseName.ShouldStartWith("IgnixaDataLayerSqlServerTest_", Case.Sensitive);
        using var pooledConnection = new SqlConnection(builder.ConnectionString);
        SqlConnection.ClearPool(pooledConnection);
        builder.InitialCatalog = "master";
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        // Only the fixture's GUID-named database is changed; ALTER DATABASE cannot parameterize its name.
#pragma warning disable CA2100
        command.CommandText =
            $"ALTER DATABASE [{databaseName}] SET READ_COMMITTED_SNAPSHOT {(enabled ? "ON" : "OFF")} WITH ROLLBACK IMMEDIATE";
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync();
        SqlConnection.ClearPool(pooledConnection);

        var actual = await Database.ExecuteScalarAsync<int>(
            "SELECT CAST(is_read_committed_snapshot_on AS int) FROM sys.databases WHERE database_id = DB_ID()");
        actual.ShouldBe(enabled ? 1 : 0);
        output.WriteLine($"Database: {databaseName}; READ_COMMITTED_SNAPSHOT: {actual}.");
    }

    private async Task<bool> WaitForCompletionOrBlockingAsync(
        Task operation, int blockedRequests, CancellationToken cancellationToken)
    {
        while (!operation.IsCompleted)
        {
            var blocked = await Database.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE database_id = DB_ID() AND blocking_session_id > 0",
                cancellationToken);
            if (blocked >= blockedRequests)
            {
                return true;
            }

            // Poll SQL's observed lock state, not a guessed delay between the racing operations.
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        }

        await operation;
        return false;
    }

    private SqlServerSourceEventStore CreateStore(ISqlExecutionService sql)
        => new(sql, Database.TenantId, NullLogger<SqlServerSourceEventStore>.Instance);

    private static NewSourceEvent Event(string stream, string package)
        => new(stream, nameof(PackageDeactivated), new PackageDeactivated(package, "1", "concurrent append"));

    private static async Task<List<SourceEvent>> ReadFromAsync(
        SqlServerSourceEventStore store, long afterEventId, CancellationToken cancellationToken)
    {
        var events = new List<SourceEvent>();
        await foreach (var item in store.ReadFromAsync(afterEventId, cancellationToken))
        {
            events.Add(item);
        }

        return events;
    }

    private sealed class PausingExecutionService(ISqlExecutionService inner, bool pauseAfterAllInserts) : ISqlExecutionService
    {
        private int _insertCommands;

        public TaskCompletionSource Paused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int InsertedCount { get; private set; }

        public async Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
            int tenantId, SqlCommand command, Func<SqlDataReader, TResult> readRow, CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
        {
            var rows = await inner.ExecuteReaderAsync(tenantId, command, readRow, cancellationToken, idempotency);
            // The exact original store used one auto-committed command rather than the transaction API.
            if (IsEventInsert(command))
            {
                InsertedCount += rows.Count;
                await PauseAsync(cancellationToken);
            }

            return rows;
        }

        public Task<int> ExecuteNonQueryAsync(
            int tenantId, SqlCommand command, CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
            => inner.ExecuteNonQueryAsync(tenantId, command, cancellationToken, idempotency);

        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            int tenantId, Func<ISqlTransactionContext, CancellationToken, Task<TResult>> work, CancellationToken cancellationToken)
            => inner.ExecuteInTransactionAsync(
                tenantId,
                async (transaction, token) =>
                {
                    var result = await work(new PausingTransactionContext(transaction, this), token);
                    if (pauseAfterAllInserts)
                    {
                        await PauseAsync(token);
                    }

                    return result;
                },
                cancellationToken);

        public Task ExecuteInTransactionAsync(
            int tenantId, Func<ISqlTransactionContext, CancellationToken, Task> work, CancellationToken cancellationToken)
            => ExecuteInTransactionAsync<object?>(tenantId, async (transaction, token) =>
            {
                await work(transaction, token);
                return null;
            }, cancellationToken);

        private async Task PauseAsync(CancellationToken cancellationToken)
        {
            Paused.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }

        private async Task AfterInsertAsync(int count, CancellationToken cancellationToken)
        {
            InsertedCount += count;
            if (++_insertCommands == 1 && !pauseAfterAllInserts)
            {
                await PauseAsync(cancellationToken);
            }
        }

        private static bool IsEventInsert(SqlCommand command)
            => command.CommandText.StartsWith("INSERT INTO dbo.SourceEvents", StringComparison.Ordinal);

        private sealed class PausingTransactionContext(
            ISqlTransactionContext inner, PausingExecutionService owner) : ISqlTransactionContext
        {
            public Task<int> ExecuteNonQueryAsync(SqlCommand command, CancellationToken cancellationToken)
                => inner.ExecuteNonQueryAsync(command, cancellationToken);

            public async Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
                SqlCommand command, Func<SqlDataReader, TResult> readRow, CancellationToken cancellationToken)
            {
                var rows = await inner.ExecuteReaderAsync(command, readRow, cancellationToken);
                if (IsEventInsert(command))
                {
                    await owner.AfterInsertAsync(rows.Count, cancellationToken);
                }

                return rows;
            }
        }
    }
}
