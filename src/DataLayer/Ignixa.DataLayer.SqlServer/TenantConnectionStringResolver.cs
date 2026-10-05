using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Domain.Models;
using Microsoft.Data.SqlClient;

namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// The single place that decides which database a tenant's SQL Server traffic goes to: the storage-type
/// gate, the system partition's connection-string inheritance, and the parse guard.
/// <para>
/// There were three copies of these rules -- this one, <c>SqlServerTenantConnectionResolver</c>, and an
/// inline third in <see cref="SqlExecutionService"/> that applied neither inheritance nor the legacy storage
/// alias. The inline copy is the one every query connection went through, so a tenant configuration that
/// routed fine through <c>CompositeRepositoryFactory</c> hard-failed the moment it reached SQL. All three
/// call sites now come here.
/// </para>
/// </summary>
public static class TenantConnectionStringResolver
{
    /// <summary>
    /// Resolves the connection string <paramref name="tenantId"/>'s data lives behind.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The tenant does not exist or is inactive, is not configured for SQL Server storage, has no connection
    /// string it can use or inherit, or has one that cannot be parsed.
    /// </exception>
    public static async Task<string> ResolveAsync(
        ITenantConfigurationStore tenantConfigurationStore, int tenantId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantConfigurationStore);

        var tenant = await tenantConfigurationStore.GetTenantConfigurationAsync(tenantId, cancellationToken);
        if (tenant is null)
        {
            // Reached for the system partition whenever configuration binding drops it -- a nested
            // property that fails to convert (e.g. a boolean supplied for the int
            // Storage.InheritConnectionStringFromTenant) makes ConfigurationBinder discard the whole
            // element rather than the one property. Name the tenant and both of its causes so the
            // message points at the configuration rather than at the query that happened to hit it.
            throw new InvalidOperationException(
                $"Tenant {tenantId} does not exist or is inactive. Check that the Tenants array in configuration " +
                $"contains an entry with TenantId {tenantId} and that every property on it binds to its declared " +
                "type -- an element with one unconvertible property is dropped from the bound list entirely.");
        }

        // "SqlEntityFramework" and "SqlServer" are synonyms for "this tenant's data lives in SQL Server"
        // throughout the codebase (see CompositeRepositoryFactory and CompositeSearchServiceFactory's
        // "SqlEntityFramework" or "SqlServer" pattern-match arms). "SqlEntityFramework" is the legacy value
        // deployed tenant configurations and App Service environment variables still carry, not a different
        // storage backend; it outlives the data layer it was named after because rejecting it here -- while
        // the composite factories accept it -- is exactly the routes-then-throws inconsistency this type
        // exists to remove.
        if (!IsSqlServerStorage(tenant.Storage.Type))
        {
            throw new InvalidOperationException(
                $"Tenant {tenantId} is configured for storage type '{tenant.Storage.Type}', not 'SqlServer' or " +
                "'SqlEntityFramework' -- SQL Server storage can only be used for tenants configured for it.");
        }

        var connectionString = tenant.Storage.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = await InheritConnectionStringAsync(
                tenantConfigurationStore, tenant, tenantId, cancellationToken);
        }

        // Parsed for its side effect only: the raw string, not the builder's normalised rendering of it,
        // is what every caller hands to SqlConnection/DacFx.
        EnsureParsable(connectionString, tenantId);
        return connectionString;
    }

    /// <summary>
    /// <see cref="ResolveAsync"/> plus the one rule only schema deployment needs: the string must name a
    /// target database. Every DacFx caller reads <c>InitialCatalog</c> off the result and hands it over as
    /// the deploy target, where an empty one surfaces as <c>CREATE DATABASE []</c> or an opaque DacFx error
    /// naming no tenant. The query path deliberately does not impose this -- a connection string that relies
    /// on the login's default database still connects, and rejecting it there would be a behaviour change
    /// for deployments that work today.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// As <see cref="ResolveAsync"/>, or the resolved connection string names no database.
    /// </exception>
    public static async Task<string> ResolveForSchemaDeploymentAsync(
        ITenantConfigurationStore tenantConfigurationStore, int tenantId, CancellationToken cancellationToken)
    {
        var connectionString = await ResolveAsync(tenantConfigurationStore, tenantId, cancellationToken);

        if (string.IsNullOrWhiteSpace(EnsureParsable(connectionString, tenantId).InitialCatalog))
        {
            throw new InvalidOperationException(
                $"Tenant {tenantId}'s connection string specifies no database name (Initial Catalog/Database). " +
                "Schema deployment needs an explicit target database.");
        }

        return connectionString;
    }

    private static async Task<string> InheritConnectionStringAsync(
        ITenantConfigurationStore tenantConfigurationStore,
        TenantConfiguration tenant,
        int tenantId,
        CancellationToken cancellationToken)
    {
        // Only the system partition (Tenant 0) may leave ConnectionString empty: it inherits from another
        // tenant's database so single-tenant deployments avoid extra infrastructure. Inverting this
        // condition would let any tenant missing its connection string silently read and write tenant 1's
        // database -- a data-isolation breach rather than a startup error.
        var isSystemPartitionAccess = tenant.IsSystemPartition || tenantId == SystemConstants.SystemPartitionId;
        if (!isSystemPartitionAccess)
        {
            throw new InvalidOperationException(
                $"Tenant {tenantId} is configured for SQL Server storage but has no ConnectionString.");
        }

        var inheritFromTenantId = tenant.Storage.InheritConnectionStringFromTenant;
        var inheritedConfig = await tenantConfigurationStore.GetTenantConfigurationAsync(inheritFromTenantId, cancellationToken);
        var inheritedConnectionString = inheritedConfig?.Storage.ConnectionString;

        if (inheritedConfig is null || string.IsNullOrWhiteSpace(inheritedConnectionString))
        {
            throw new InvalidOperationException(
                $"System partition (Tenant {tenantId}) has no ConnectionString and cannot inherit from Tenant {inheritFromTenantId} " +
                $"(tenant {(inheritedConfig is null ? "not found" : "has no ConnectionString")}).");
        }

        // Validate the SOURCE tenant's storage type too, not just the requesting one. Without this, a
        // system partition declared "SqlServer" silently inherits the connection string of a tenant
        // configured for an entirely different backend (InheritConnectionStringFromTenant defaults to 1),
        // and the wrong string flows all the way to the server before failing as an opaque ADO.NET or DacFx
        // error naming neither tenant.
        if (!IsSqlServerStorage(inheritedConfig.Storage.Type))
        {
            throw new InvalidOperationException(
                $"System partition (Tenant {tenantId}) cannot inherit its ConnectionString from Tenant {inheritFromTenantId}: " +
                $"that tenant is configured for storage type '{inheritedConfig.Storage.Type}', not SQL Server.");
        }

        return inheritedConnectionString;
    }

    // A malformed connection string is the most likely appsettings typo, and both SqlConnectionStringBuilder
    // and SqlConnection surface it as an ArgumentException naming no tenant. Translating it here keeps this
    // type's contract "failures are InvalidOperationException naming the tenant" for every caller.
    private static SqlConnectionStringBuilder EnsureParsable(string connectionString, int tenantId)
    {
        try
        {
            return new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                $"Tenant {tenantId}'s connection string could not be parsed. Check the tenant's Storage.ConnectionString value.", ex);
        }
    }

    private static bool IsSqlServerStorage(string storageType)
        => storageType is "SqlServer" or "SqlEntityFramework";
}
