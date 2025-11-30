using Microsoft.Extensions.Caching.Memory;

namespace Ignixa.Domain.Caching;

/// <summary>
/// In-memory cache for FHIR conformance resources (L1 tier).
/// Suitable for single-instance deployments or as part of a 2-tier cache strategy.
/// </summary>
public class InMemoryConformanceCache : IFhirConformanceCache
{
    private readonly IMemoryCache _cache;
    private readonly MemoryCacheEntryOptions _defaultCacheOptions;

    /// <summary>
    /// Initializes a new instance of the InMemoryConformanceCache class.
    /// </summary>
    /// <param name="cache">Memory cache instance</param>
    public InMemoryConformanceCache(IMemoryCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));

        // Default cache options: 24 hour absolute expiration, 1 hour sliding expiration
        _defaultCacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24),
            SlidingExpiration = TimeSpan.FromHours(1)
        };
    }

    /// <summary>
    /// Retrieves a cached conformance resource.
    /// </summary>
    public ValueTask<string?> GetAsync(
        string tenantId,
        string canonical,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Tenant ID cannot be null or empty", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(canonical))
            throw new ArgumentException("Canonical URL cannot be null or empty", nameof(canonical));

        var key = BuildCacheKey(tenantId, canonical, version);

        if (_cache.TryGetValue(key, out var cachedValue))
        {
            return ValueTask.FromResult((string?)cachedValue);
        }

        return ValueTask.FromResult<string?>(null);
    }

    /// <summary>
    /// Caches a conformance resource.
    /// </summary>
    public ValueTask SetAsync(
        string tenantId,
        string canonical,
        string resourceJson,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Tenant ID cannot be null or empty", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(canonical))
            throw new ArgumentException("Canonical URL cannot be null or empty", nameof(canonical));
        if (string.IsNullOrWhiteSpace(resourceJson))
            throw new ArgumentException("Resource JSON cannot be null or empty", nameof(resourceJson));

        var key = BuildCacheKey(tenantId, canonical, version: null);
        var cacheOptions = ttl.HasValue
            ? new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }
            : _defaultCacheOptions;

        _cache.Set(key, resourceJson, cacheOptions);

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Caches multiple conformance resources in a single operation.
    /// </summary>
    public ValueTask SetManyAsync(
        string tenantId,
        IReadOnlyDictionary<string, string> resources,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Tenant ID cannot be null or empty", nameof(tenantId));
        if (resources == null || resources.Count == 0)
            throw new ArgumentException("Resources dictionary cannot be null or empty", nameof(resources));

        var cacheOptions = ttl.HasValue
            ? new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }
            : _defaultCacheOptions;

        foreach (var kvp in resources)
        {
            if (string.IsNullOrWhiteSpace(kvp.Key) || string.IsNullOrWhiteSpace(kvp.Value))
                continue;

            var key = BuildCacheKey(tenantId, kvp.Key, version: null);
            _cache.Set(key, kvp.Value, cacheOptions);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Invalidates a cached resource.
    /// </summary>
    public ValueTask InvalidateAsync(
        string tenantId,
        string canonical,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Tenant ID cannot be null or empty", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(canonical))
            throw new ArgumentException("Canonical URL cannot be null or empty", nameof(canonical));

        var key = BuildCacheKey(tenantId, canonical, version: null);
        _cache.Remove(key);

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Clears all cached resources for a tenant.
    /// </summary>
    /// <remarks>
    /// Note: In-memory cache doesn't have direct tenant-wide clear capability.
    /// This is a placeholder for distributed cache implementations.
    /// </remarks>
    public ValueTask InvalidateTenantAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Tenant ID cannot be null or empty", nameof(tenantId));

        // In-memory cache doesn't support prefix-based eviction.
        // This would be handled by distributed cache (Redis) in production.
        // For now, this is a no-op for in-memory cache.

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Builds a cache key from tenant, canonical, and optional version.
    /// </summary>
    private static string BuildCacheKey(string tenantId, string canonical, string? version)
    {
        return version != null
            ? $"conformance:{tenantId}:{canonical}|{version}"
            : $"conformance:{tenantId}:{canonical}";
    }
}
