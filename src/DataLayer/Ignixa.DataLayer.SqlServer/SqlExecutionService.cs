using Ignixa.Domain.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer;

public sealed class SqlExecutionService : ISqlExecutionService
{
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
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
        int tenantId,
        SqlCommand command,
        Func<SqlDataReader, TResult> readRow,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("Added in Task 3.");

    public Task<int> ExecuteNonQueryAsync(
        int tenantId,
        SqlCommand command,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("Added in Task 3.");
}
