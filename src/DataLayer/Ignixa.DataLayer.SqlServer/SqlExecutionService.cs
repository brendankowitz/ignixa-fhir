using Ignixa.Domain.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Ignixa.DataLayer.SqlServer;

public sealed class SqlExecutionService : ISqlExecutionService
{
    private static readonly ResiliencePipeline TransientFaultPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<SqlException>(IsTransient),
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(200),
            BackoffType = DelayBackoffType.Exponential,
        })
        .Build();

    private readonly ITenantConfigurationStore _tenantConfigurationStore;
    private readonly ILogger<SqlExecutionService> _logger;

    public SqlExecutionService(ITenantConfigurationStore tenantConfigurationStore, ILogger<SqlExecutionService> logger)
    {
        ArgumentNullException.ThrowIfNull(tenantConfigurationStore);
        ArgumentNullException.ThrowIfNull(logger);
        _tenantConfigurationStore = tenantConfigurationStore;
        _logger = logger;
    }

    internal async Task<SqlConnection> OpenConnectionAsync(int tenantId, CancellationToken cancellationToken)
    {
        var connectionString = await SqlServerTenantConnectionResolver.ResolveConnectionStringAsync(
            _tenantConfigurationStore, tenantId, cancellationToken);
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public async Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
        int tenantId,
        SqlCommand command,
        Func<SqlDataReader, TResult> readRow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(readRow);

        return await TransientFaultPipeline.ExecuteAsync(async ct =>
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

    public async Task<int> ExecuteNonQueryAsync(
        int tenantId,
        SqlCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await TransientFaultPipeline.ExecuteAsync(async ct =>
        {
            await using var connection = await OpenConnectionAsync(tenantId, ct);
            command.Connection = connection;

            var affected = await command.ExecuteNonQueryAsync(ct);
            _logger.LogDebug("Executed non-query for tenant {TenantId}, {AffectedRows} row(s) affected", tenantId, affected);
            return affected;
        }, cancellationToken);
    }

    private static bool IsTransient(SqlException ex) => IsTransient(ex.Number);

    // Transient SQL Server error numbers: -2 (timeout), 4060 (cannot open database, may be
    // transient during failover), 40197/40501/40613 (Azure SQL throttling/failover), 10928/10929
    // (Azure SQL resource limits), 1205 (deadlock victim). Internal (not private) so Task 4's test
    // can assert on it directly without needing to construct a real SqlException, which has no
    // public constructor with a settable Number.
    internal static bool IsTransient(int sqlErrorNumber)
        => sqlErrorNumber is -2 or 1205 or 4060 or 10928 or 10929 or 40197 or 40501 or 40613;
}
