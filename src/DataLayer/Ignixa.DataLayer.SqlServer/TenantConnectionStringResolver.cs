using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Microsoft.Data.SqlClient;

namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Resolves a tenant's raw SQL Server connection string for callers that need the string itself:
/// DacFx's DacServices/SqlPackage APIs, and the raw ADO.NET code in SchemaDeployer,
/// SchemaVersionResolver, and Ignixa.SchemaUpgrade.Cli. <see cref="ISqlExecutionService"/> is not
/// a fit for those callers -- it deliberately exposes only ExecuteReaderAsync/ExecuteNonQueryAsync
/// and owns its connections internally, so it can neither hand out a connection string nor run
/// DacFx. Kept as a separate seam rather than widening that interface, so schema-deployment
/// concerns don't leak into the general query-execution contract.
/// </summary>
public static class TenantConnectionStringResolver
{
    public static async Task<string> ResolveAsync(
        ITenantConfigurationStore tenantConfigurationStore, int tenantId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantConfigurationStore);

        var tenant = await tenantConfigurationStore.GetTenantConfigurationAsync(tenantId, cancellationToken);
        if (tenant is null)
        {
            throw new InvalidOperationException($"Tenant {tenantId} does not exist or is inactive.");
        }

        // "SqlEntityFramework" and "SqlServer" are synonyms for "this tenant's data lives in SQL
        // Server" in the repository/search factory paths (see
        // SqlEntityFrameworkRepositoryFactory's identical check, and
        // CompositeRepositoryFactory/CompositeSearchServiceFactory's "SqlEntityFramework" or
        // "SqlServer" pattern-match arms) -- "SqlEntityFramework" is the legacy/actual value every
        // real tenant config in this repo uses today, not a different storage backend.
        if (!IsSqlServerStorage(tenant.Storage.Type))
        {
            throw new InvalidOperationException(
                $"Tenant {tenantId} is configured for storage type '{tenant.Storage.Type}', not 'SqlServer' or " +
                "'SqlEntityFramework' -- schema deployment can only run for tenants configured for SQL Server storage.");
        }

        var connectionString = tenant.Storage.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // System partition (Tenant 0) is allowed a null ConnectionString: it inherits from
            // another tenant's database (single-tenant deployments avoid extra infrastructure).
            // Mirrors the null-connection-string block of
            // SqlEntityFrameworkRepositoryFactory.GetOrCreateFactoryAsync -- kept in sync
            // deliberately, not duplicated by accident. Note this resolver deliberately does NOT
            // mirror that method's ValidateManagedIdentityAuthentication call: operators running
            // the schema-upgrade CLI against a tenant database use SQL auth, so the two are not
            // interchangeable.
            var isSystemPartitionAccess = tenant.IsSystemPartition || tenantId == SystemConstants.SystemPartitionId;
            if (!isSystemPartitionAccess)
            {
                throw new InvalidOperationException(
                    $"Tenant {tenantId} is configured for 'SqlServer' storage but has no ConnectionString.");
            }

            var inheritFromTenantId = tenant.Storage.InheritConnectionStringFromTenant;
            var inheritedConfig = await tenantConfigurationStore.GetTenantConfigurationAsync(inheritFromTenantId, cancellationToken);

            if (inheritedConfig is null || string.IsNullOrWhiteSpace(inheritedConfig.Storage.ConnectionString))
            {
                throw new InvalidOperationException(
                    $"System partition (Tenant {tenantId}) has no ConnectionString and cannot inherit from Tenant {inheritFromTenantId} " +
                    $"(tenant {(inheritedConfig is null ? "not found" : "has no ConnectionString")}).");
            }

            // Validate the SOURCE tenant's storage type too, not just the requesting one. Without
            // this, a system partition declared "SqlServer" silently inherits the connection string
            // of a tenant configured for an entirely different backend (InheritConnectionStringFromTenant
            // defaults to 1), and the wrong string flows all the way into dacServices.Deploy before
            // failing as an opaque DacFx/ADO.NET error naming neither tenant.
            if (!IsSqlServerStorage(inheritedConfig.Storage.Type))
            {
                throw new InvalidOperationException(
                    $"System partition (Tenant {tenantId}) cannot inherit its ConnectionString from Tenant {inheritFromTenantId}: " +
                    $"that tenant is configured for storage type '{inheritedConfig.Storage.Type}', not SQL Server.");
            }

            connectionString = inheritedConfig.Storage.ConnectionString;
        }

        // Every caller immediately does new SqlConnectionStringBuilder(cs).InitialCatalog and hands
        // the result to DacFx as the target database name. An empty catalog would surface as
        // "CREATE DATABASE []" or an opaque DacFx error; catching it at this single shared choke
        // point gives one actionable message naming the tenant instead of three obscure ones.
        if (string.IsNullOrWhiteSpace(new SqlConnectionStringBuilder(connectionString).InitialCatalog))
        {
            throw new InvalidOperationException(
                $"Tenant {tenantId}'s connection string specifies no database name (Initial Catalog/Database). " +
                "Schema deployment needs an explicit target database.");
        }

        return connectionString;
    }

    private static bool IsSqlServerStorage(string storageType)
        => storageType is "SqlServer" or "SqlEntityFramework";
}
