using Ignixa.Domain.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer;

public sealed class SchemaVersionResolver : ISchemaVersionResolver
{
    private readonly ITenantConfigurationStore _tenantConfigurationStore;
    private readonly ILogger<SchemaVersionResolver> _logger;

    public SchemaVersionResolver(ITenantConfigurationStore tenantConfigurationStore, ILogger<SchemaVersionResolver> logger)
    {
        ArgumentNullException.ThrowIfNull(tenantConfigurationStore);
        ArgumentNullException.ThrowIfNull(logger);
        _tenantConfigurationStore = tenantConfigurationStore;
        _logger = logger;
    }

    public async Task<int> GetCurrentVersionAsync(int tenantId, CancellationToken cancellationToken)
    {
        var connectionString = await TenantConnectionStringResolver.ResolveForSchemaDeploymentAsync(
            _tenantConfigurationStore, tenantId, cancellationToken);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // An un-versioned pre-Phase-C tenant (deployed before the SchemaVersion table was
        // introduced) has no dbo.SchemaVersion table at all -- querying it directly throws
        // SqlException "Invalid object name 'dbo.SchemaVersion'" rather than returning a row.
        // Mirrors SchemaDeployer.IsDatabaseEmptyAsync's own sys.tables existence check.
        if (!await SchemaVersionTableExistsAsync(connection, cancellationToken))
        {
            _logger.LogDebug("Tenant {TenantId} has no SchemaVersion table yet; treating as schema version 0.", tenantId);
            return 0;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ISNULL(MAX(Version), 0) FROM dbo.SchemaVersion";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        var version = (int)result!;
        _logger.LogDebug("Tenant {TenantId}'s current schema version is {Version}.", tenantId, version);
        return version;
    }

    private static async Task<bool> SchemaVersionTableExistsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SchemaVersion') THEN 1 ELSE 0 END";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return (int)result! == 1;
    }
}
