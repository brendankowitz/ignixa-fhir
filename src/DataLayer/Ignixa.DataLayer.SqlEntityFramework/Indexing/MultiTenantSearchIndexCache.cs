// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlEntityFramework.Indexing;

/// <summary>
/// Singleton cache manager for multi-tenant search index reference data.
/// Maintains per-tenant SearchIndexReferenceDataCache instances to ensure isolation between tenants.
/// Uses on-demand caching for large datasets (Systems, QuantityCodes) to prevent memory exhaustion.
/// </summary>
/// <remarks>
/// Design Principles:
/// - Tenant Isolation: Each tenant gets its own cache instance to prevent cross-tenant data leaks
/// - On-Demand Caching: Large tables (Systems, QuantityCodes) are cached on-demand, not bulk loaded
/// - Limited Preloading: Only small reference data (ResourceTypes, SearchParams with limit) is preloaded
/// - Memory Safety: Prevents OOM by avoiding bulk loading of 100K+ rows
///
/// Cache Lifecycle:
/// - Singleton lifetime: Lives for the entire application lifecycle
/// - Per-tenant caches: Created on first access, reused for subsequent requests
/// - Cache invalidation: Tenants can be invalidated individually without affecting others
///
/// Thread Safety:
/// - Cache creation is thread-safe using ConcurrentDictionary
/// - DbContext is owned by each cache instance (not shared across threads)
/// - Factory delegates are thread-safe
/// </remarks>
public class MultiTenantSearchIndexCache
{
    private readonly ConcurrentDictionary<int, CacheEntry> _tenantCaches = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MultiTenantSearchIndexCache> _logger;

    /// <summary>
    /// Container for tenant-specific cache instance and its DbContext factory.
    /// </summary>
    private class CacheEntry
    {
        public required SearchIndexReferenceDataCache Cache { get; init; }
        public required DbContextOptions<FhirDbContext> DbContextOptions { get; init; }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiTenantSearchIndexCache"/> class.
    /// </summary>
    /// <param name="loggerFactory">Factory for creating logger instances.</param>
    public MultiTenantSearchIndexCache(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<MultiTenantSearchIndexCache>();

        _logger.LogInformation("MultiTenantSearchIndexCache initialized");
    }

    /// <summary>
    /// Gets or creates the cache instance for the specified tenant.
    /// Cache instances are created lazily on first access and reused for subsequent requests.
    /// Thread-safe: Multiple threads can call this method concurrently.
    /// </summary>
    /// <param name="tenantId">The tenant ID.</param>
    /// <param name="dbContextOptions">DbContext options for this tenant (only used on first access).</param>
    /// <returns>The tenant-specific SearchIndexReferenceDataCache instance.</returns>
    public SearchIndexReferenceDataCache GetOrCreateCacheForTenant(
        int tenantId,
        DbContextOptions<FhirDbContext> dbContextOptions)
    {
        var entry = _tenantCaches.GetOrAdd(tenantId, tid =>
        {
            _logger.LogInformation("Creating new cache instance for tenant {TenantId}", tid);

            // Create DbContext for this tenant (used for cache lookups and preloading)
            // DbContext is owned by the cache instance and will be disposed when cache is invalidated
            var dbContext = new FhirDbContext(dbContextOptions);

            var cache = new SearchIndexReferenceDataCache(
                dbContext,
                _loggerFactory.CreateLogger<SearchIndexReferenceDataCache>());

            // Initialize the cache by batch-loading all search parameters
            // This prevents N+1 query problems during startup
            cache.InitializeAsync().GetAwaiter().GetResult();

            _logger.LogDebug("Cache instance created for tenant {TenantId}", tid);

            return new CacheEntry
            {
                Cache = cache,
                DbContextOptions = dbContextOptions
            };
        });

        return entry.Cache;
    }

    /// <summary>
    /// Invalidates and removes the cache for the specified tenant.
    /// Call this when tenant configuration changes or when cache needs to be refreshed.
    /// </summary>
    /// <param name="tenantId">The tenant ID to invalidate.</param>
    /// <returns>True if cache was found and removed, false if no cache existed for this tenant.</returns>
    public bool InvalidateTenantCache(int tenantId)
    {
        if (_tenantCaches.TryRemove(tenantId, out var entry))
        {
            _logger.LogInformation("Invalidating cache for tenant {TenantId}", tenantId);

            // Dispose the cache (releases DbContext)
            entry.Cache.Dispose();

            _logger.LogDebug("Cache invalidated for tenant {TenantId}", tenantId);
            return true;
        }

        _logger.LogWarning("Attempted to invalidate cache for tenant {TenantId}, but no cache existed", tenantId);
        return false;
    }

    /// <summary>
    /// Invalidates all tenant caches.
    /// Useful for global cache refresh or shutdown scenarios.
    /// </summary>
    public void InvalidateAllCaches()
    {
        _logger.LogInformation("Invalidating all tenant caches ({Count} tenants)", _tenantCaches.Count);

        foreach (var kvp in _tenantCaches)
        {
            kvp.Value.Cache.Dispose();
        }

        _tenantCaches.Clear();

        _logger.LogInformation("All tenant caches invalidated");
    }

    /// <summary>
    /// Gets the number of currently cached tenants.
    /// Useful for monitoring and diagnostics.
    /// </summary>
    public int CachedTenantCount => _tenantCaches.Count;

    /// <summary>
    /// Gets statistics for all cached tenants.
    /// Useful for monitoring memory usage and cache effectiveness.
    /// </summary>
    public IReadOnlyDictionary<int, CacheStatistics> CacheStatistics
    {
        get
        {
            var stats = new Dictionary<int, CacheStatistics>();

            foreach (var kvp in _tenantCaches)
            {
                stats[kvp.Key] = kvp.Value.Cache.GetStatistics();
            }

            return stats;
        }
    }
}

/// <summary>
/// Statistics for a single tenant's cache.
/// </summary>
public record CacheStatistics
{
    /// <summary>Number of cached search parameters.</summary>
    public int SearchParamCount { get; init; }

    /// <summary>Number of cached resource types.</summary>
    public int ResourceTypeCount { get; init; }

    /// <summary>Number of cached systems (on-demand).</summary>
    public int SystemCount { get; init; }

    /// <summary>Number of cached quantity codes (on-demand).</summary>
    public int QuantityCodeCount { get; init; }
}
