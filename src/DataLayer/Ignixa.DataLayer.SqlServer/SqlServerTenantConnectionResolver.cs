using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;

namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Single home for the two rules that decide which database a tenant's SQL Server traffic goes to:
/// the storage-type gate, and the system partition's connection-string inheritance.
/// <para>
/// Both rules previously existed twice -- once inline in
/// <c>SqlEntityFrameworkRepositoryFactory.GetOrCreateFactoryAsync</c> and once in
/// <see cref="SqlExecutionService"/>, whose own comments said they were "kept in sync deliberately".
/// Deliberate duplication is still duplication: the two copies disagreed on their exception text and
/// nothing structurally stopped them drifting on substance. Both now call this.
/// </para>
/// </summary>
public static class SqlServerTenantConnectionResolver
{
    /// <summary>
    /// Resolves the connection string for <paramref name="tenantId"/>, applying the storage-type gate and
    /// the system partition's inheritance rule.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The tenant does not exist, is not configured for SQL Server storage, or has no connection string it
    /// can use or inherit.
    /// </exception>
    public static async Task<string> ResolveConnectionStringAsync(
        ITenantConfigurationStore tenantConfigurationStore,
        int tenantId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantConfigurationStore);

        var tenant = await tenantConfigurationStore.GetTenantConfigurationAsync(tenantId, cancellationToken);
        if (tenant is null)
        {
            throw new InvalidOperationException($"Tenant {tenantId} does not exist or is inactive.");
        }

        // "SqlEntityFramework" and "SqlServer" are synonyms for "this tenant's data lives in SQL
        // Server" throughout the codebase (see CompositeRepositoryFactory and
        // CompositeSearchServiceFactory's "SqlEntityFramework" or "SqlServer" pattern-match arms).
        // "SqlEntityFramework" is the legacy value older tenant configs carry, not a different storage
        // backend, so it is still accepted even though shipped configuration no longer emits it.
        if (tenant.Storage.Type != "SqlServer" && tenant.Storage.Type != "SqlEntityFramework")
        {
            throw new InvalidOperationException(
                $"Tenant {tenantId} is configured for storage type '{tenant.Storage.Type}', not 'SqlServer' -- " +
                "ISqlExecutionService can only be used for tenants configured for SQL Server storage.");
        }

        var connectionString = tenant.Storage.ConnectionString;
        if (!string.IsNullOrEmpty(connectionString))
        {
            return connectionString;
        }

        // System partition (Tenant 0) is allowed a null ConnectionString: it inherits from another
        // tenant's database (single-tenant deployments avoid extra infrastructure).
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

        return inheritedConfig.Storage.ConnectionString;
    }
}
