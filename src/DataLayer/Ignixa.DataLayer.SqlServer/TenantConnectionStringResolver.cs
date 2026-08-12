using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;

namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Resolves a tenant's raw SQL Server connection string for callers that need the string itself
/// (DacFx's DacServices/SqlPackage APIs, and raw ADO.NET code in SchemaDeployer/
/// SchemaVersionResolver/Ignixa.SchemaUpgrade.Cli) rather than an open, retry-wrapped
/// <see cref="Microsoft.Data.SqlClient.SqlConnection"/> as <see cref="ISqlExecutionService"/>
/// hands back. Split out from ISqlExecutionService (which narrowed to just
/// tenant-existence/storage-type validation plus opening a connection) so that widening
/// ISqlExecutionService's surface for schema-deployment callers doesn't leak DacFx/raw-ADO.NET
/// concerns back into it.
/// </summary>
public static class TenantConnectionStringResolver
{
    public static async Task<string> ResolveAsync(
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
                "schema deployment can only run for tenants configured for SQL Server storage.");
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
}
