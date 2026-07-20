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
        var connectionString = await SqlExecutionService.ResolveConnectionStringAsync(
            _tenantConfigurationStore, tenantId, cancellationToken);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ISNULL(MAX(Version), 0) FROM dbo.SchemaVersion";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        var version = (int)result!;
        _logger.LogDebug("Tenant {TenantId}'s current schema version is {Version}.", tenantId, version);
        return version;
    }
}
