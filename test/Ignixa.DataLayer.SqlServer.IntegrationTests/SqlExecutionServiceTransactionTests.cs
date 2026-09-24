using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

/// <summary>
/// The transaction primitive, against a real server. Before it existed, every multi-statement operation in
/// this layer was a sequence of independently auto-committed statements on independent connections, and a
/// failure part-way left the earlier ones applied.
/// <para>
/// These need a real SQL Server because that is the only thing that can answer the questions being asked:
/// whether a rollback actually undid the writes, and whether a retried unit of work applied its insert once
/// or twice. A fake connection would only re-assert the code's own control flow.
/// </para>
/// </summary>
public sealed class SqlExecutionServiceTransactionTests
{
    private sealed class SingleTenantStore(string connectionString) : ITenantConfigurationStore
    {
        private readonly TenantConfiguration _tenant = new()
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "4.0",
            Storage = new TenantStorageConfiguration { Type = "SqlServer", ConnectionString = connectionString },
        };

        public TenantMode Mode => TenantMode.Isolated;

        public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken ct = default)
            => new(tenantId == 1 ? _tenant : null);

        public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(CancellationToken ct = default)
            => new((IReadOnlyList<TenantConfiguration>)new List<TenantConfiguration> { _tenant });

        public ValueTask<TenantConfiguration?> ResolveByHostAsync(string host, CancellationToken cancellationToken = default)
            => new((TenantConfiguration?)null);
    }

    private const int TenantId = 1;

    private static string GetConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING");
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new SkipException(
                "TEST_SQL_CONNECTION_STRING is not set (see docker-compose.test.yml) -- skipping, not failing.");
        }

        return connectionString;
    }

    private static SqlExecutionService CreateService()
    {
        return new SqlExecutionService(
            new SingleTenantStore(GetConnectionString()),
            // Development: TEST_SQL_CONNECTION_STRING is a SQL-auth string, which the Production credential
            // guard exists to reject. SqlExecutionServiceConnectionResolutionTests covers the guard itself.
            new ManagedIdentityConnectionStringValidator("Development", NullLogger<ManagedIdentityConnectionStringValidator>.Instance),
            NullLogger<SqlExecutionService>.Instance);
    }

    /// <summary>
    /// A GUID-named permanent table. Not a temp table, for the reason
    /// <see cref="SqlExecutionServiceExecutionTests"/> documents at length: every call opens its own pooled
    /// connection, and sp_reset_connection drops both # and ## temp tables on logical reuse.
    /// </summary>
    private sealed class ScratchTable : IAsyncDisposable
    {
        private readonly SqlExecutionService _service;

        private ScratchTable(SqlExecutionService service, string name)
        {
            _service = service;
            Name = name;
        }

        public string Name { get; }

        public static async Task<ScratchTable> CreateAsync(SqlExecutionService service)
        {
            var name = $"TxnTest_{Guid.NewGuid():N}";
#pragma warning disable CA2100
            await using var create = new SqlCommand($"CREATE TABLE {name} (Id INT NOT NULL PRIMARY KEY)");
#pragma warning restore CA2100
            await service.ExecuteNonQueryAsync(TenantId, create, CancellationToken.None);
            return new ScratchTable(service, name);
        }

        public SqlCommand Insert(int id)
        {
#pragma warning disable CA2100
            var command = new SqlCommand($"INSERT INTO {Name} (Id) VALUES (@id)");
#pragma warning restore CA2100
            command.Parameters.AddWithValue("@id", id);
            return command;
        }

        public async Task<IReadOnlyList<int>> ReadIdsAsync()
        {
#pragma warning disable CA2100
            await using var select = new SqlCommand($"SELECT Id FROM {Name} ORDER BY Id");
#pragma warning restore CA2100
            return await _service.ExecuteReaderAsync(TenantId, select, reader => reader.GetInt32(0), CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
#pragma warning disable CA2100
            await using var drop = new SqlCommand($"IF OBJECT_ID('dbo.{Name}') IS NOT NULL DROP TABLE {Name}");
#pragma warning restore CA2100
            await _service.ExecuteNonQueryAsync(TenantId, drop, CancellationToken.None);
        }
    }

    /// <summary>
    /// A genuine <see cref="SqlException"/> with <c>Number == -2</c>, taken from a real client-side command
    /// timeout. SqlException has no public constructor, and a hand-rolled stand-in would not be classified
    /// by the pipeline's own <c>IsTransient</c> predicate -- which is the thing under test.
    /// </summary>
    private static async Task<SqlException> CaptureTransientSqlExceptionAsync(SqlExecutionService service)
    {
        await using var command = new SqlCommand("WAITFOR DELAY '00:00:05'") { CommandTimeout = 1 };
        var ex = await Should.ThrowAsync<SqlException>(() => service.ExecuteNonQueryAsync(
            TenantId, command, CancellationToken.None, SqlCommandIdempotency.NonIdempotent));
        ex.Number.ShouldBe(-2);
        return ex;
    }

    [SkippableFact]
    public async Task GivenSeveralCommands_WhenTheUnitOfWorkSucceeds_ThenEveryWriteIsCommittedTogether()
    {
        var service = CreateService();
        await using var table = await ScratchTable.CreateAsync(service);

        await service.ExecuteInTransactionAsync(
            TenantId,
            async (transaction, ct) =>
            {
                await using var first = table.Insert(1);
                await transaction.ExecuteNonQueryAsync(first, ct);
                await using var second = table.Insert(2);
                await transaction.ExecuteNonQueryAsync(second, ct);
            },
            CancellationToken.None);

        (await table.ReadIdsAsync()).ShouldBe([1, 2]);
    }

    [SkippableFact]
    public async Task GivenSeveralCommands_WhenALaterOneFails_ThenTheEarlierWritesAreRolledBack()
    {
        var service = CreateService();
        await using var table = await ScratchTable.CreateAsync(service);

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => service.ExecuteInTransactionAsync(
            TenantId,
            async (transaction, ct) =>
            {
                await using var first = table.Insert(1);
                await transaction.ExecuteNonQueryAsync(first, ct);
                throw new InvalidOperationException("the unit of work failed after its first write");
            },
            CancellationToken.None));

        ex.Message.ShouldContain("failed after its first write");
        (await table.ReadIdsAsync()).ShouldBeEmpty();
    }

    /// <summary>
    /// Cancellation is the path a rollback is easiest to lose: the token that would carry the rollback
    /// command is the one that has just been cancelled.
    /// </summary>
    [SkippableFact]
    public async Task GivenTheCallerCancelsMidUnitOfWork_WhenTheTransactionUnwinds_ThenTheEarlierWritesAreRolledBack()
    {
        var service = CreateService();
        await using var table = await ScratchTable.CreateAsync(service);
        using var cts = new CancellationTokenSource();

        await Should.ThrowAsync<OperationCanceledException>(() => service.ExecuteInTransactionAsync(
            TenantId,
            async (transaction, ct) =>
            {
                await using var first = table.Insert(1);
                await transaction.ExecuteNonQueryAsync(first, ct);
                await cts.CancelAsync();
                ct.ThrowIfCancellationRequested();
            },
            cts.Token));

        (await table.ReadIdsAsync()).ShouldBeEmpty();
    }

    /// <summary>
    /// The requirement the whole design turns on: a retry must restart the unit, never resume it. If the
    /// pipeline retried without a rollback -- or retried a partially committed transaction -- the row
    /// inserted on the first attempt would still be there and the table would hold it twice.
    /// </summary>
    [SkippableFact]
    public async Task GivenATransientFailureMidTransaction_WhenThePipelineRetries_ThenTheUnitRestartsAndTheWriteIsAppliedOnce()
    {
        var service = CreateService();
        await using var table = await ScratchTable.CreateAsync(service);
        var transientFailure = await CaptureTransientSqlExceptionAsync(service);
        var attempts = 0;

        await service.ExecuteInTransactionAsync(
            TenantId,
            async (transaction, ct) =>
            {
                attempts++;
                await using var insert = table.Insert(1);
                await transaction.ExecuteNonQueryAsync(insert, ct);

                if (attempts == 1)
                {
                    throw transientFailure;
                }
            },
            CancellationToken.None);

        attempts.ShouldBe(2);

        // One row, and the PRIMARY KEY did not have to save us: a second attempt that resumed rather than
        // restarted would have failed on the duplicate key instead of reaching here.
        (await table.ReadIdsAsync()).ShouldBe([1]);
    }

    /// <summary>
    /// Reads inside the unit of work must run on the transaction's own connection, or they cannot see its
    /// uncommitted writes and every read-then-write sequence silently works off stale state.
    /// </summary>
    [SkippableFact]
    public async Task GivenAWriteFollowedByARead_WhenBothRunInTheTransaction_ThenTheReadSeesTheUncommittedWrite()
    {
        var service = CreateService();
        await using var table = await ScratchTable.CreateAsync(service);

        var seen = await service.ExecuteInTransactionAsync(
            TenantId,
            async (transaction, ct) =>
            {
                await using var insert = table.Insert(7);
                await transaction.ExecuteNonQueryAsync(insert, ct);

#pragma warning disable CA2100
                await using var select = new SqlCommand($"SELECT Id FROM {table.Name}");
#pragma warning restore CA2100
                return await transaction.ExecuteReaderAsync(select, reader => reader.GetInt32(0), ct);
            },
            CancellationToken.None);

        seen.ShouldBe([7]);
    }

    /// <summary>
    /// A failed commit is the one outcome that must never be retried: the server may have committed and lost
    /// the acknowledgement, so re-running the unit could apply it twice.
    /// <para>
    /// The failure is produced by killing the transaction's own session from a second connection once its
    /// work is done, which is the real shape of the problem -- a connection lost between "the work
    /// succeeded" and "the commit was acknowledged". Dooming the transaction with XACT_ABORT does not work
    /// for this: SQL Server detects the uncommittable transaction at the end of the batch and fails the
    /// command instead, so the failure never reaches the commit.
    /// </para>
    /// </summary>
    [SkippableFact]
    public async Task GivenTheConnectionIsLostBeforeTheCommit_WhenTheUnitOfWorkCompletes_ThenItIsReportedAsIndeterminateAndNotRetried()
    {
        var service = CreateService();
        await using var table = await ScratchTable.CreateAsync(service);
        var attempts = 0;

        var ex = await Should.ThrowAsync<SqlTransactionCommitException>(() => service.ExecuteInTransactionAsync(
            TenantId,
            async (transaction, ct) =>
            {
                attempts++;

                await using var insert = table.Insert(1);
                await transaction.ExecuteNonQueryAsync(insert, ct);

                await using var spidCommand = new SqlCommand("SELECT @@SPID");
                var spids = await transaction.ExecuteReaderAsync(spidCommand, reader => reader.GetInt16(0), ct);

                await KillSessionAsync(spids[0], ct);
            },
            CancellationToken.None));

        attempts.ShouldBe(1);
        ex.TenantId.ShouldBe(TenantId);
        ex.Message.ShouldContain("unknown");
        ex.InnerException.ShouldNotBeNull();

        // The server rolled the killed session's transaction back, so the insert is gone. That is what makes
        // "unknown" the honest word: this run happened not to commit, and nothing on the client could have
        // told it apart from one that did.
        (await table.ReadIdsAsync()).ShouldBeEmpty();
    }

    private static async Task KillSessionAsync(short sessionId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        // CA2100 suppressed: sessionId is a short read back from @@SPID on this test's own connection, and
        // KILL does not accept a parameter for it.
#pragma warning disable CA2100
        command.CommandText = $"KILL {sessionId}";
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
