using System.Collections.Concurrent;
using Ignixa.Domain.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Ignixa.DataLayer.SqlServer;

public sealed class SqlExecutionService : ISqlExecutionService
{
    private readonly ITenantConfigurationStore _tenantConfigurationStore;
    private readonly ManagedIdentityConnectionStringValidator _connectionStringValidator;
    private readonly ILogger<SqlExecutionService> _logger;
    private readonly ResiliencePipeline _transientFaultPipeline;

    // Tenant -> the connection string most recently validated for it. The credential guard is a string scan
    // plus an informational log; running it on every command would put both on the hot path, and
    // SqlServerTenantServiceFactory only ever ran it once per tenant. Keyed on the resolved string rather
    // than a plain "already validated" flag so a configuration change that swaps a tenant's credentials is
    // re-validated instead of inheriting the old verdict. Bounded by the tenant count.
    private readonly ConcurrentDictionary<int, string> _validatedConnectionStrings = new();

    public SqlExecutionService(
        ITenantConfigurationStore tenantConfigurationStore,
        ManagedIdentityConnectionStringValidator connectionStringValidator,
        ILogger<SqlExecutionService> logger)
    {
        ArgumentNullException.ThrowIfNull(tenantConfigurationStore);
        ArgumentNullException.ThrowIfNull(connectionStringValidator);
        ArgumentNullException.ThrowIfNull(logger);
        _tenantConfigurationStore = tenantConfigurationStore;
        _connectionStringValidator = connectionStringValidator;
        _logger = logger;

        // Instance-scoped (not static) so OnRetry can log through this instance's logger.
        _transientFaultPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<SqlException>(IsTransient),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        args.Outcome.Exception,
                        "Transient SQL error on attempt {AttemptNumber}, retrying after {RetryDelay}",
                        args.AttemptNumber + 1,
                        args.RetryDelay);
                    return default;
                },
            })
            .Build();
    }

    internal async Task<SqlConnection> OpenConnectionAsync(int tenantId, CancellationToken cancellationToken)
    {
        var connectionString = await ResolveValidatedConnectionStringAsync(tenantId, cancellationToken);

        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Resolves the tenant's connection string through the one shared resolver -- so the system partition's
    /// inheritance rule and the legacy "SqlEntityFramework" storage alias apply here exactly as they do to
    /// schema deployment and the repository factory -- and puts the Production credential guard on the path
    /// every connection actually takes.
    /// <para>
    /// The guard used to live only in <c>SqlServerTenantServiceFactory</c>, on the FHIR-repository path.
    /// Everything else reaching a tenant database through this service -- the package repository, the event
    /// store, the background-job repository, terminology lookup and import -- bypassed it entirely. A check
    /// that can be walked around is not a check, so it moved to where connections are opened.
    /// </para>
    /// </summary>
    private async Task<string> ResolveValidatedConnectionStringAsync(int tenantId, CancellationToken cancellationToken)
    {
        var connectionString = await TenantConnectionStringResolver.ResolveAsync(
            _tenantConfigurationStore, tenantId, cancellationToken);

        if (!_validatedConnectionStrings.TryGetValue(tenantId, out var lastValidated) ||
            !string.Equals(lastValidated, connectionString, StringComparison.Ordinal))
        {
            // Recorded only after Validate returns: a rejected connection string must be rejected again on
            // the next attempt, not cached as "seen".
            _connectionStringValidator.Validate(connectionString, tenantId);
            _validatedConnectionStrings[tenantId] = connectionString;
        }

        return connectionString;
    }

    public async Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
        int tenantId,
        SqlCommand command,
        Func<SqlDataReader, TResult> readRow,
        CancellationToken cancellationToken,
        SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(readRow);

        async ValueTask<IReadOnlyList<TResult>> Operation(CancellationToken ct)
        {
            await using var connection = await OpenConnectionAsync(tenantId, ct);
            command.Connection = connection;

            var results = new List<TResult>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                results.Add(readRow(reader));
            }

            _logger.LogDebug("Executed reader for tenant {TenantId}, {RowCount} row(s)", tenantId, results.Count);
            return results;
        }

        try
        {
            return await ExecuteWithRetryPolicyAsync(Operation, idempotency, cancellationToken);
        }
        catch (SqlException ex)
        {
            // Non-transient errors (e.g. constraint violations) reach here on the first attempt and
            // are often expected control flow for callers (conditional create, optimistic
            // concurrency) -- log at Warning, not Error, to avoid paging on-call for expected
            // failures. Transient errors reaching here means every retry was exhausted.
            LogExecutionFailure(ex, tenantId);
            throw;
        }
    }

    public async Task<int> ExecuteNonQueryAsync(
        int tenantId,
        SqlCommand command,
        CancellationToken cancellationToken,
        SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
    {
        ArgumentNullException.ThrowIfNull(command);

        async ValueTask<int> Operation(CancellationToken ct)
        {
            await using var connection = await OpenConnectionAsync(tenantId, ct);
            command.Connection = connection;

            var affected = await command.ExecuteNonQueryAsync(ct);
            _logger.LogDebug("Executed non-query for tenant {TenantId}, {AffectedRows} row(s) affected", tenantId, affected);
            return affected;
        }

        try
        {
            return await ExecuteWithRetryPolicyAsync(Operation, idempotency, cancellationToken);
        }
        catch (SqlException ex)
        {
            // See the comment in ExecuteReaderAsync -- non-transient errors are expected control
            // flow for many callers and shouldn't page on-call at Error severity.
            LogExecutionFailure(ex, tenantId);
            throw;
        }
    }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        int tenantId,
        Func<ISqlTransactionContext, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        Func<CancellationToken, ValueTask<TResult>> operation =
            async ct => await RunTransactionAsync(tenantId, work, ct);

        try
        {
            // The whole unit is the retry unit. A transient fault raised anywhere before the commit has
            // already been rolled back by RunTransactionAsync, so re-running the unit starts from an
            // unchanged database -- which is exactly what makes retrying a multi-statement write safe here
            // and unsafe for the single-command methods above. A failed commit is wrapped in a
            // SqlTransactionCommitException, which is not a SqlException and so never reaches this pipeline.
            return await _transientFaultPipeline.ExecuteAsync(operation, cancellationToken);
        }
        catch (SqlException ex)
        {
            LogExecutionFailure(ex, tenantId);
            throw;
        }
    }

    public Task ExecuteInTransactionAsync(
        int tenantId,
        Func<ISqlTransactionContext, CancellationToken, Task> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        return ExecuteInTransactionAsync<object?>(
            tenantId,
            async (context, ct) =>
            {
                await work(context, ct);
                return null;
            },
            cancellationToken);
    }

    private async Task<TResult> RunTransactionAsync<TResult>(
        int tenantId,
        Func<ISqlTransactionContext, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(tenantId, cancellationToken);

        // await using on both: disposal is the backstop that rolls back if an explicit rollback below could
        // not run (a torn connection, an exception from the rollback itself, or a cancellation between the
        // two). SqlTransaction.DisposeAsync rolls back an uncommitted transaction.
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        TResult result;
        try
        {
            result = await work(new SqlTransactionContext(connection, transaction), cancellationToken);
        }
        catch
        {
            await RollbackAsync(transaction, tenantId);
            throw;
        }

        try
        {
            // CancellationToken.None deliberately: the work has succeeded and the commit is in flight. A
            // token cancelled mid-commit cannot un-send it -- it only leaves us unable to say whether the
            // server applied it, which is the one outcome this whole design exists to avoid. The command
            // timeout still bounds the wait.
            await transaction.CommitAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Committing the transaction for tenant {TenantId} failed", tenantId);
            await RollbackAsync(transaction, tenantId);
            throw new SqlTransactionCommitException(tenantId, ex);
        }

        _logger.LogDebug("Committed transaction for tenant {TenantId}", tenantId);
        return result;
    }

    private async Task RollbackAsync(SqlTransaction transaction, int tenantId)
    {
        try
        {
            // CancellationToken.None: an already-cancelled caller is the case that needs the rollback most.
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is InvalidOperationException or SqlException)
        {
            // The transaction is already gone -- the connection was torn down, or the server rolled it back
            // itself (a deadlock victim, 1205, arrives this way). Nothing is left to undo. Log it and let
            // the original failure propagate rather than masking it with this one.
            _logger.LogWarning(
                ex,
                "Rolling back the transaction for tenant {TenantId} failed; it is not committed",
                tenantId);
        }
    }

    private ValueTask<TResult> ExecuteWithRetryPolicyAsync<TResult>(
        Func<CancellationToken, ValueTask<TResult>> operation,
        SqlCommandIdempotency idempotency,
        CancellationToken cancellationToken)
        => idempotency == SqlCommandIdempotency.NonIdempotent
            ? operation(cancellationToken)
            : _transientFaultPipeline.ExecuteAsync(operation, cancellationToken);

    private void LogExecutionFailure(SqlException ex, int tenantId)
    {
        var logLevel = IsTransient(ex.Number) ? LogLevel.Error : LogLevel.Warning;
        _logger.Log(logLevel, ex, "SQL execution failed for tenant {TenantId} (SqlErrorNumber={SqlErrorNumber})", tenantId, ex.Number);
    }

    private static bool IsTransient(SqlException ex) => IsTransient(ex.Number);

    // Transient SQL Server / Azure SQL error numbers, per Microsoft's documented transient-fault
    // guidance (https://learn.microsoft.com/azure/azure-sql/database/troubleshoot-common-errors-issues
    // and .../troubleshoot-common-connectivity-issues):
    //  -2                  command/connection timeout (client-side, Microsoft.Data.SqlClient)
    //  1205                deadlock victim -- transaction guaranteed rolled back
    //  4060                cannot open database, may be transient during failover
    //  4221                login to read-secondary failed (row-versioning transition)
    //  615                 stale in-memory database-ID cache after a detach/reattach race
    //  926                 database marked SUSPECT during the last stage of a reconfiguration
    //  10928/10929         Azure SQL resource-governance limits (worker threads / session count)
    //  40197/40501/40613   Azure SQL throttling, busy service, and failover
    //  49918/49919/49920   Azure SQL resource-governance limits (requests / create-or-update ops)
    //  233/64/10053/10054/10060
    //                      transport-level connection resets (pipe closed, connection aborted or
    //                      forcibly closed by the remote host, connection attempt timed out)
    //  258                 connection-establishment timeout ("TCP Provider... wait operation timed
    //                      out"), distinct from -2's command/connection-open timeout; produced by
    //                      Microsoft.Data.SqlClient when the initial TCP handshake doesn't complete
    //                      within the connection timeout window.
    // Internal (not private) so tests can assert on it directly without needing to construct a
    // real SqlException, which has no public constructor with a settable Number.
    internal static bool IsTransient(int sqlErrorNumber)
        => sqlErrorNumber is -2 or 1205 or 4060 or 4221 or 615 or 926
            or 10928 or 10929 or 40197 or 40501 or 40613 or 49918 or 49919 or 49920
            or 233 or 64 or 10053 or 10054 or 10060 or 258;

    private sealed class SqlTransactionContext(SqlConnection connection, SqlTransaction transaction)
        : ISqlTransactionContext
    {
        public async Task<int> ExecuteNonQueryAsync(SqlCommand command, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);
            Enlist(command);
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
            SqlCommand command,
            Func<SqlDataReader, TResult> readRow,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);
            ArgumentNullException.ThrowIfNull(readRow);
            Enlist(command);

            var results = new List<TResult>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(readRow(reader));
            }

            return results;
        }

        // Both assignments are required: Microsoft.Data.SqlClient throws if a command carries a connection
        // with an open local transaction but no Transaction of its own.
        private void Enlist(SqlCommand command)
        {
            command.Connection = connection;
            command.Transaction = transaction;
        }
    }
}
