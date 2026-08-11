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
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            return await _transientFaultPipeline.ExecuteAsync(async ct =>
            {
                await using var connection = await OpenConnectionAsync(tenantId, ct);
                command.Connection = connection;

                var affected = await command.ExecuteNonQueryAsync(ct);
                _logger.LogDebug("Executed non-query for tenant {TenantId}, {AffectedRows} row(s) affected", tenantId, affected);
                return affected;
            }, cancellationToken);
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

    // Transient SQL Server error numbers: -2 (timeout), 4060 (cannot open database, may be
    // transient during failover), 40197/40501/40613 (Azure SQL throttling/failover), 10928/10929
    // (Azure SQL resource limits), 1205 (deadlock victim). Internal (not private) so tests can
    // assert on it directly without needing to construct a real SqlException, which has no
    // public constructor with a settable Number.
    internal static bool IsTransient(int sqlErrorNumber)
        => sqlErrorNumber is -2 or 1205 or 4060 or 10928 or 10929 or 40197 or 40501 or 40613;
}
