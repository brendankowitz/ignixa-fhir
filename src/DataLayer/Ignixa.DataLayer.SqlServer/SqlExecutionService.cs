using Ignixa.Domain.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Ignixa.DataLayer.SqlServer;

public sealed class SqlExecutionService : ISqlExecutionService
{
    private readonly ITenantConfigurationStore _tenantConfigurationStore;
    private readonly ILogger<SqlExecutionService> _logger;
    private readonly ResiliencePipeline _transientFaultPipeline;

    public SqlExecutionService(ITenantConfigurationStore tenantConfigurationStore, ILogger<SqlExecutionService> logger)
    {
        ArgumentNullException.ThrowIfNull(tenantConfigurationStore);
        ArgumentNullException.ThrowIfNull(logger);
        _tenantConfigurationStore = tenantConfigurationStore;
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
        var tenant = await _tenantConfigurationStore.GetTenantConfigurationAsync(tenantId, cancellationToken);
        if (tenant is null)
        {
            throw new InvalidOperationException($"Tenant {tenantId} does not exist or is inactive.");
        }

        if (tenant.Storage.Type != "SqlServer")
        {
            throw new InvalidOperationException(
                $"Tenant {tenantId} is configured for storage type '{tenant.Storage.Type}', not 'SqlServer' -- " +
                "ISqlExecutionService can only be used for tenants configured for SQL Server storage.");
        }

        if (string.IsNullOrEmpty(tenant.Storage.ConnectionString))
        {
            // TODO(Phase C/D): partition 0 (the system tenant) is documented/implemented elsewhere
            // (TenantConfiguration.InheritConnectionStringFromTenant, SqlEntityFrameworkRepositoryFactory)
            // to inherit its connection string from another tenant when left empty. That resolution
            // is deferred to the SqlServerTenantConnectionResolver consolidation in a later phase;
            // nothing calls OpenConnectionAsync for tenant 0 yet, so this is a tracked gap, not a
            // silent one.
            throw new InvalidOperationException(
                $"Tenant {tenantId} is configured for 'SqlServer' storage but has no ConnectionString.");
        }

        var connection = new SqlConnection(tenant.Storage.ConnectionString);
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

    public async Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
        int tenantId,
        SqlCommand command,
        Func<SqlDataReader, TResult> readRow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(readRow);

        try
        {
            return await _transientFaultPipeline.ExecuteAsync(async ct =>
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
                return (IReadOnlyList<TResult>)results;
            }, cancellationToken);
        }
        catch (SqlException ex)
        {
            // Non-transient errors (e.g. constraint violations) reach here on the first attempt and
            // are often expected control flow for callers (conditional create, optimistic
            // concurrency) -- log at Warning, not Error, to avoid paging on-call for expected
            // failures. Transient errors reaching here means every retry was exhausted.
            var logLevel = IsTransient(ex.Number) ? LogLevel.Error : LogLevel.Warning;
            _logger.Log(logLevel, ex, "SQL execution failed for tenant {TenantId} (SqlErrorNumber={SqlErrorNumber})", tenantId, ex.Number);
            throw;
        }
    }

    public async Task<int> ExecuteNonQueryAsync(
        int tenantId,
        SqlCommand command,
        CancellationToken cancellationToken,
        bool disableRetries = false)
    {
        ArgumentNullException.ThrowIfNull(command);

        Func<CancellationToken, ValueTask<int>> operation = async ct =>
        {
            await using var connection = await OpenConnectionAsync(tenantId, ct);
            command.Connection = connection;

            var affected = await command.ExecuteNonQueryAsync(ct);
            _logger.LogDebug("Executed non-query for tenant {TenantId}, {AffectedRows} row(s) affected", tenantId, affected);
            return affected;
        };

        try
        {
            // disableRetries lets a caller opt out for commands whose side effects aren't safe to
            // execute more than once and which the caller isn't prepared to make idempotent (e.g.
            // via an idempotency key) -- a transient error like a timeout doesn't guarantee the
            // server didn't already commit the statement, so blind retry can duplicate a write.
            return disableRetries
                ? await operation(cancellationToken)
                : await _transientFaultPipeline.ExecuteAsync(operation, cancellationToken);
        }
        catch (SqlException ex)
        {
            // See the comment in ExecuteReaderAsync -- non-transient errors are expected control
            // flow for many callers and shouldn't page on-call at Error severity.
            var logLevel = IsTransient(ex.Number) ? LogLevel.Error : LogLevel.Warning;
            _logger.Log(logLevel, ex, "SQL execution failed for tenant {TenantId} (SqlErrorNumber={SqlErrorNumber})", tenantId, ex.Number);
            throw;
        }
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
}
