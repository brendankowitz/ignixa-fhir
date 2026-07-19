using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
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

    internal static async Task<string> ResolveConnectionStringAsync(
        ITenantConfigurationStore tenantConfigurationStore, int tenantId, CancellationToken cancellationToken)
    {
        var tenant = await tenantConfigurationStore.GetTenantConfigurationAsync(tenantId, cancellationToken);
        if (tenant is null)
        {
            throw new InvalidOperationException($"Tenant {tenantId} does not exist or is inactive.");
        }

        // "SqlEntityFramework" and "SqlServer" are synonyms for "this tenant's data lives in SQL
        // Server" throughout the codebase (see SqlEntityFrameworkRepositoryFactory's identical check,
        // and CompositeRepositoryFactory/CompositeSearchServiceFactory's "SqlEntityFramework" or
        // "SqlServer" pattern-match arms) -- "SqlEntityFramework" is the legacy/actual value every
        // real tenant config in this repo uses today, not a different storage backend.
        if (tenant.Storage.Type != "SqlServer" && tenant.Storage.Type != "SqlEntityFramework")
        {
            throw new InvalidOperationException(
                $"Tenant {tenantId} is configured for storage type '{tenant.Storage.Type}', not 'SqlServer' -- " +
                "ISqlExecutionService can only be used for tenants configured for SQL Server storage.");
        }

        var connectionString = tenant.Storage.ConnectionString;
        if (string.IsNullOrEmpty(connectionString))
        {
            // System partition (Tenant 0) is allowed a null ConnectionString: it inherits from
            // another tenant's database (single-tenant deployments avoid extra infrastructure).
            // Mirrors SqlEntityFrameworkRepositoryFactory.GetOrCreateFactoryAsync's identical
            // inheritance logic -- kept in sync deliberately, not duplicated by accident.
            var isSystemPartitionAccess = tenant.IsSystemPartition || tenantId == SystemConstants.SystemPartitionId;
            if (!isSystemPartitionAccess)
            {
                throw new InvalidOperationException(
                    $"Tenant {tenantId} is configured for 'SqlServer' storage but has no ConnectionString.");
            }

            var inheritFromTenantId = tenant.Storage.InheritConnectionStringFromTenant;
            var inheritedConfig = await tenantConfigurationStore.GetTenantConfigurationAsync(inheritFromTenantId, cancellationToken);

            if (inheritedConfig is null || string.IsNullOrEmpty(inheritedConfig.Storage.ConnectionString))
            {
                throw new InvalidOperationException(
                    $"System partition (Tenant {tenantId}) has no ConnectionString and cannot inherit from Tenant {inheritFromTenantId} " +
                    $"(tenant {(inheritedConfig == null ? "not found" : "has no ConnectionString")}).");
            }

            connectionString = inheritedConfig.Storage.ConnectionString;
        }

        return connectionString;
    }

    internal async Task<SqlConnection> OpenConnectionAsync(int tenantId, CancellationToken cancellationToken)
    {
        var connectionString = await ResolveConnectionStringAsync(_tenantConfigurationStore, tenantId, cancellationToken);
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
