using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Search.Definition;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Brings one tenant's database to a state where a repository or search service may safely be handed out:
/// schema deployed, schema upgraded, search-parameter catalog seeded, reference data preloaded -- in that
/// order, once per tenant.
/// <para>
/// <b>Why the order is the deliverable, not an implementation detail.</b> Each step is a precondition of
/// the next. Nothing can be read or written before the schema exists; the catalog seed writes into
/// <c>dbo.SearchParam</c>, which the upgrade may have altered; and the write path's row generators read
/// <c>SearchParameterMappings</c> off the cache and <b>silently drop an index row</b> for any parameter
/// missing from it, so a repository handed out before the seed indexes resources incompletely and reports
/// success. That failure is invisible until a search returns nothing.
/// </para>
/// <para>
/// <b>Seed and preload are one step against one instance, deliberately.</b> The seed used to run against
/// the EF cache and the preload against the SqlServer one -- two caches, one of which the write path never
/// consulted. <see cref="SqlServerSearchIndexCacheRegistry"/> now owns the single per-tenant instance, and
/// it seeds authoritative definitions and preloads on creation. Core definitions are then seeded against
/// that same instance without replacing its authority. Both seeding and preloading populate the one
/// instance the write path reads; standalone caches without authoritative definitions adopt the core
/// manager here and can subsequently gain package definitions through synchronization.
/// </para>
/// </summary>
public sealed class SqlServerTenantInitializer(
    ISchemaDeployer schemaDeployer,
    SqlServerSearchIndexCacheRegistry cacheRegistry,
    ILogger<SqlServerTenantInitializer> logger)
{
    private readonly ISchemaDeployer _schemaDeployer = schemaDeployer ?? throw new ArgumentNullException(nameof(schemaDeployer));
    private readonly SqlServerSearchIndexCacheRegistry _cacheRegistry = cacheRegistry ?? throw new ArgumentNullException(nameof(cacheRegistry));
    private readonly ILogger<SqlServerTenantInitializer> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public Task<SqlServerSearchIndexReferenceDataCache> GetReferenceDataCacheAsync(
        int tenantId, CancellationToken cancellationToken) =>
        _cacheRegistry.GetOrCreateAsync(tenantId, cancellationToken);

    /// <summary>
    /// Runs the four initialization steps for <paramref name="tenantId"/> and returns the tenant's shared
    /// reference data cache, ready for the write and search paths.
    /// </summary>
    /// <param name="searchParameterDefinitionManager">
    /// Supplies the core definitions to seed alongside the cache's authoritative tenant definitions.
    /// Adopted as the definition manager only when the cache does not already have one.
    /// </param>
    public async Task<SqlServerSearchIndexReferenceDataCache> InitializeAsync(
        int tenantId,
        ISearchParameterDefinitionManager searchParameterDefinitionManager,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(searchParameterDefinitionManager);

        await DeploySchemaAsync(tenantId, cancellationToken);

        var cache = await GetReferenceDataCacheAsync(tenantId, cancellationToken);

        var syncedCount = await cache.SeedSearchParametersToDatabaseAsync(
            searchParameterDefinitionManager, cancellationToken);

        _logger.LogInformation(
            "Search parameter catalog seeded for tenant {TenantId}: {SyncedCount} new URLs",
            tenantId,
            syncedCount);

        return cache;
    }

    private async Task DeploySchemaAsync(int tenantId, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Ensuring database schema is deployed for tenant {TenantId}...", tenantId);

            await _schemaDeployer.DeployIfEmptyAsync(tenantId, cancellationToken);
            _logger.LogInformation("Database schema deployment completed for tenant {TenantId}", tenantId);

            await _schemaDeployer.UpgradeIfNeededAsync(tenantId, cancellationToken);
            _logger.LogInformation("Database schema upgrade check completed for tenant {TenantId}", tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to deploy database schema for tenant {TenantId}. Error: {Message}",
                tenantId,
                ex.Message);
            throw;
        }
    }
}
