# Standardized Caching Architecture

This document outlines the caching abstraction design for FHIR Server v2, supporting both in-memory and distributed caching scenarios with memory-efficient patterns and multi-tenant isolation.

## Core Caching Abstractions

### Base Cache Interface

```csharp
public interface ICache
{
    /// <summary>
    /// Get cached value by key
    /// </summary>
    ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set cached value with optional expiration
    /// </summary>
    ValueTask SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove cached value by key
    /// </summary>
    ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove multiple cached values by pattern
    /// </summary>
    ValueTask RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if key exists in cache
    /// </summary>
    ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get multiple values by keys
    /// </summary>
    ValueTask<IReadOnlyDictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set multiple values with same expiration
    /// </summary>
    ValueTask SetManyAsync<T>(IReadOnlyDictionary<string, T> keyValues, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
}
```

### Tenant-Aware Cache Interface

```csharp
public interface ITenantCache
{
    /// <summary>
    /// Get cache scoped to specific tenant
    /// </summary>
    ICache GetTenantCache(string tenantId);

    /// <summary>
    /// Clear all cache entries for a tenant
    /// </summary>
    ValueTask ClearTenantAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get cache statistics per tenant
    /// </summary>
    ValueTask<TenantCacheStatistics> GetTenantStatisticsAsync(string tenantId, CancellationToken cancellationToken = default);
}

public record TenantCacheStatistics(
    string TenantId,
    long ItemCount,
    long MemoryUsageBytes,
    long HitCount,
    long MissCount,
    double HitRatio);
```

### Memory-Efficient Cache Key Builder

```csharp
public static class CacheKeyBuilder
{
    private static readonly CompositeFormat ResourceKeyFormat = CompositeFormat.Parse("resource:{0}:{1}:{2}");
    private static readonly CompositeFormat SearchKeyFormat = CompositeFormat.Parse("search:{0}:{1}");
    private static readonly CompositeFormat SchemaKeyFormat = CompositeFormat.Parse("schema:{0}:{1}");

    public static string BuildResourceKey(string tenantId, string resourceType, string resourceId) =>
        string.Create(CultureInfo.InvariantCulture, ResourceKeyFormat, tenantId, resourceType, resourceId);

    public static string BuildSearchKey(string tenantId, ReadOnlySpan<char> searchQuery)
    {
        // Use span-based hashing for memory efficiency
        var hash = XxHash64.HashToUInt64(MemoryMarshal.AsBytes(searchQuery));
        return string.Create(CultureInfo.InvariantCulture, SearchKeyFormat, tenantId, hash);
    }

    public static string BuildSchemaKey(string tenantId, string version) =>
        string.Create(CultureInfo.InvariantCulture, SchemaKeyFormat, tenantId, version);

    public static string BuildTransactionKey(string tenantId, TransactionId transactionId) =>
        $"tx:{tenantId}:{transactionId}";

    public static string BuildCapabilityKey(string tenantId, string version) =>
        $"capability:{tenantId}:{version}";
}
```

## In-Memory Cache Implementation

### High-Performance Memory Cache

```csharp
public class MemoryEfficientCache : ICache, IDisposable
{
    private readonly MemoryCache _cache;
    private readonly ILogger<MemoryEfficientCache> _logger;
    private readonly MemoryCacheOptions _options;

    public MemoryEfficientCache(IOptions<MemoryCacheOptions> options, ILogger<MemoryEfficientCache> logger)
    {
        _options = options.Value;
        _logger = logger;
        _cache = new MemoryCache(_options);
    }

    public ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(key, out var cached))
        {
            if (cached is T typedValue)
            {
                return ValueTask.FromResult<T?>(typedValue);
            }

            if (cached is CacheEntry<T> entry)
            {
                return ValueTask.FromResult<T?>(entry.Value);
            }
        }

        return ValueTask.FromResult<T?>(default);
    }

    public ValueTask SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        var options = new MemoryCacheEntryOptions();

        if (expiration.HasValue)
        {
            options.AbsoluteExpirationRelativeToNow = expiration.Value;
        }

        // Use appropriate size for memory pressure calculation
        if (value is string str)
        {
            options.Size = str.Length * 2; // UTF-16 character size
        }
        else if (value is ReadOnlyMemory<byte> memory)
        {
            options.Size = memory.Length;
        }
        else if (value is IMemoryMeasurable measurable)
        {
            options.Size = measurable.EstimateMemoryUsage();
        }

        // Register callback for cache eviction logging
        options.RegisterPostEvictionCallback((evictedKey, evictedValue, reason, state) =>
        {
            if (reason != EvictionReason.Replaced)
            {
                _logger.LogDebug("Cache entry evicted: Key={Key}, Reason={Reason}", evictedKey, reason);
            }
        });

        var entry = new CacheEntry<T>(value, DateTimeOffset.UtcNow);
        _cache.Set(key, entry, options);

        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _cache.Remove(key);
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        // Memory cache doesn't support pattern-based removal efficiently
        // This is a limitation for in-memory scenarios
        _logger.LogWarning("Pattern-based removal not efficiently supported in memory cache: {Pattern}", pattern);
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var exists = _cache.TryGetValue(key, out _);
        return ValueTask.FromResult(exists);
    }

    public async ValueTask<IReadOnlyDictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, T?>();

        foreach (var key in keys)
        {
            var value = await GetAsync<T>(key, cancellationToken);
            results[key] = value;
        }

        return results;
    }

    public async ValueTask SetManyAsync<T>(IReadOnlyDictionary<string, T> keyValues, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        var tasks = keyValues.Select(kvp => SetAsync(kvp.Key, kvp.Value, expiration, cancellationToken));
        await Task.WhenAll(tasks.Select(t => t.AsTask()));
    }

    public void Dispose()
    {
        _cache?.Dispose();
    }
}

public interface IMemoryMeasurable
{
    long EstimateMemoryUsage();
}

public record CacheEntry<T>(T Value, DateTimeOffset CreatedAt);
```

### Tenant-Scoped Memory Cache

```csharp
public class TenantScopedMemoryCache : ITenantCache
{
    private readonly ConcurrentDictionary<string, ICache> _tenantCaches = new();
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TenantScopedMemoryCache> _logger;

    public TenantScopedMemoryCache(IServiceProvider serviceProvider, ILogger<TenantScopedMemoryCache> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public ICache GetTenantCache(string tenantId)
    {
        return _tenantCaches.GetOrAdd(tenantId, CreateTenantCache);
    }

    private ICache CreateTenantCache(string tenantId)
    {
        _logger.LogInformation("Creating cache for tenant {TenantId}", tenantId);

        // Create tenant-specific cache with scoped keys
        return new TenantPrefixedCache(
            _serviceProvider.GetRequiredService<ICache>(),
            tenantId);
    }

    public ValueTask ClearTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        if (_tenantCaches.TryRemove(tenantId, out var cache))
        {
            if (cache is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<TenantCacheStatistics> GetTenantStatisticsAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        // Memory cache doesn't provide detailed statistics by default
        // Return basic statistics
        var stats = new TenantCacheStatistics(
            TenantId: tenantId,
            ItemCount: 0, // Not easily available in MemoryCache
            MemoryUsageBytes: 0, // Not easily available in MemoryCache
            HitCount: 0,
            MissCount: 0,
            HitRatio: 0.0);

        return ValueTask.FromResult(stats);
    }
}

internal class TenantPrefixedCache : ICache
{
    private readonly ICache _innerCache;
    private readonly string _tenantPrefix;

    public TenantPrefixedCache(ICache innerCache, string tenantId)
    {
        _innerCache = innerCache;
        _tenantPrefix = $"tenant:{tenantId}:";
    }

    private string PrefixKey(string key) => _tenantPrefix + key;

    public ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
        _innerCache.GetAsync<T>(PrefixKey(key), cancellationToken);

    public ValueTask SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) =>
        _innerCache.SetAsync(PrefixKey(key), value, expiration, cancellationToken);

    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        _innerCache.RemoveAsync(PrefixKey(key), cancellationToken);

    public ValueTask RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default) =>
        _innerCache.RemoveByPatternAsync(_tenantPrefix + pattern, cancellationToken);

    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        _innerCache.ExistsAsync(PrefixKey(key), cancellationToken);

    public async ValueTask<IReadOnlyDictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var prefixedKeys = keys.Select(PrefixKey).ToArray();
        var prefixedResults = await _innerCache.GetManyAsync<T>(prefixedKeys, cancellationToken);

        // Remove prefixes from result keys
        var results = new Dictionary<string, T?>();
        foreach (var kvp in prefixedResults)
        {
            var originalKey = kvp.Key.Substring(_tenantPrefix.Length);
            results[originalKey] = kvp.Value;
        }

        return results;
    }

    public ValueTask SetManyAsync<T>(IReadOnlyDictionary<string, T> keyValues, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        var prefixedKeyValues = keyValues.ToDictionary(
            kvp => PrefixKey(kvp.Key),
            kvp => kvp.Value);

        return _innerCache.SetManyAsync(prefixedKeyValues, expiration, cancellationToken);
    }
}
```

## Distributed Cache Implementation (Redis)

### Redis Cache Implementation

```csharp
public class RedisCache : ICache
{
    private readonly IDatabase _database;
    private readonly ISerializer _serializer;
    private readonly ILogger<RedisCache> _logger;
    private readonly RedisCacheOptions _options;

    public RedisCache(
        IConnectionMultiplexer redis,
        ISerializer serializer,
        IOptions<RedisCacheOptions> options,
        ILogger<RedisCache> logger)
    {
        _database = redis.GetDatabase();
        _serializer = serializer;
        _options = options.Value;
        _logger = logger;
    }

    public async ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var value = await _database.StringGetAsync(key);
            if (!value.HasValue) return default;

            // Use memory-efficient deserialization
            using var stream = FhirStreamManager.GetStream(value, "CacheGet");
            return await _serializer.DeserializeAsync<T>(stream, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get cache value for key {Key}", key);
            return default;
        }
    }

    public async ValueTask SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        try
        {
            // Use memory-efficient serialization
            using var stream = FhirStreamManager.GetStream("CacheSet");
            await _serializer.SerializeAsync(stream, value, cancellationToken);
            var serializedValue = stream.ToArray();

            var expire = expiration ?? _options.DefaultExpiration;
            await _database.StringSetAsync(key, serializedValue, expire);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set cache value for key {Key}", key);
        }
    }

    public async ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await _database.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove cache value for key {Key}", key);
        }
    }

    public async ValueTask RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        try
        {
            var server = _database.Multiplexer.GetServer(_database.Multiplexer.GetEndPoints().First());
            var keys = server.Keys(pattern: pattern);

            var keyArray = keys.Take(1000).ToArray(); // Limit to prevent performance issues
            if (keyArray.Length > 0)
            {
                await _database.KeyDeleteAsync(keyArray);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove cache values by pattern {Pattern}", pattern);
        }
    }

    public async ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _database.KeyExistsAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check cache key existence {Key}", key);
            return false;
        }
    }

    public async ValueTask<IReadOnlyDictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        try
        {
            var keyArray = keys.ToArray();
            var redisKeys = keyArray.Select(k => (RedisKey)k).ToArray();
            var values = await _database.StringGetAsync(redisKeys);

            var results = new Dictionary<string, T?>();
            for (int i = 0; i < keyArray.Length; i++)
            {
                if (values[i].HasValue)
                {
                    try
                    {
                        using var stream = FhirStreamManager.GetStream(values[i], "CacheGetMany");
                        var value = await _serializer.DeserializeAsync<T>(stream, cancellationToken);
                        results[keyArray[i]] = value;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to deserialize cache value for key {Key}", keyArray[i]);
                        results[keyArray[i]] = default;
                    }
                }
                else
                {
                    results[keyArray[i]] = default;
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get multiple cache values");
            return keys.ToDictionary(k => k, _ => default(T));
        }
    }

    public async ValueTask SetManyAsync<T>(IReadOnlyDictionary<string, T> keyValues, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var expire = expiration ?? _options.DefaultExpiration;
            var tasks = keyValues.Select(async kvp =>
            {
                using var stream = FhirStreamManager.GetStream("CacheSetMany");
                await _serializer.SerializeAsync(stream, kvp.Value, cancellationToken);
                var serializedValue = stream.ToArray();

                return new KeyValuePair<RedisKey, RedisValue>(kvp.Key, serializedValue);
            });

            var redisKeyValues = await Task.WhenAll(tasks);
            await _database.StringSetAsync(redisKeyValues.ToArray());

            // Set expiration for all keys
            if (expire.HasValue)
            {
                var expireTasks = keyValues.Keys.Select(key => _database.KeyExpireAsync(key, expire));
                await Task.WhenAll(expireTasks);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set multiple cache values");
        }
    }
}

public class RedisCacheOptions
{
    public TimeSpan? DefaultExpiration { get; set; } = TimeSpan.FromHours(1);
    public string KeyPrefix { get; set; } = "fhir:";
}
```

## High-Performance Serialization

### Memory-Efficient Serializer

```csharp
public interface ISerializer
{
    ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken cancellationToken = default);
    ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default);
}

public class SystemTextJsonSerializer : ISerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public async ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken cancellationToken = default)
    {
        await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken);
    }

    public async ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default)
    {
        return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken);
    }
}
```

## Cache Layer Integration

### FHIR-Specific Cache Services

```csharp
public interface IFhirCacheService
{
    ValueTask<ResourceWrapper?> GetResourceAsync(string tenantId, string resourceType, string resourceId, CancellationToken cancellationToken = default);
    ValueTask SetResourceAsync(ResourceWrapper resource, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
    ValueTask InvalidateResourceAsync(string tenantId, string resourceType, string resourceId, CancellationToken cancellationToken = default);

    ValueTask<SearchResult?> GetSearchResultAsync(string tenantId, string searchQuery, CancellationToken cancellationToken = default);
    ValueTask SetSearchResultAsync(string tenantId, string searchQuery, SearchResult result, TimeSpan? expiration = null, CancellationToken cancellationToken = default);

    ValueTask<IFhirSchemaProvider?> GetSchemaProviderAsync(string tenantId, string version, CancellationToken cancellationToken = default);
    ValueTask SetSchemaProviderAsync(string tenantId, string version, IFhirSchemaProvider provider, CancellationToken cancellationToken = default);
}

public class FhirCacheService : IFhirCacheService
{
    private readonly ITenantCache _cache;
    private readonly FhirCacheOptions _options;

    public FhirCacheService(ITenantCache cache, IOptions<FhirCacheOptions> options)
    {
        _cache = cache;
        _options = options.Value;
    }

    public async ValueTask<ResourceWrapper?> GetResourceAsync(string tenantId, string resourceType, string resourceId, CancellationToken cancellationToken = default)
    {
        var tenantCache = _cache.GetTenantCache(tenantId);
        var key = CacheKeyBuilder.BuildResourceKey(tenantId, resourceType, resourceId);
        return await tenantCache.GetAsync<ResourceWrapper>(key, cancellationToken);
    }

    public async ValueTask SetResourceAsync(ResourceWrapper resource, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        var tenantCache = _cache.GetTenantCache(resource.TenantId);
        var key = CacheKeyBuilder.BuildResourceKey(resource.TenantId, resource.ResourceType, resource.ResourceId);
        var cacheExpiration = expiration ?? _options.ResourceCacheExpiration;

        await tenantCache.SetAsync(key, resource, cacheExpiration, cancellationToken);
    }

    public async ValueTask InvalidateResourceAsync(string tenantId, string resourceType, string resourceId, CancellationToken cancellationToken = default)
    {
        var tenantCache = _cache.GetTenantCache(tenantId);
        var key = CacheKeyBuilder.BuildResourceKey(tenantId, resourceType, resourceId);
        await tenantCache.RemoveAsync(key, cancellationToken);
    }

    // Implement other methods similarly...
}

public class FhirCacheOptions
{
    public TimeSpan ResourceCacheExpiration { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan SearchCacheExpiration { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan SchemaCacheExpiration { get; set; } = TimeSpan.FromHours(4);
    public TimeSpan CapabilityCacheExpiration { get; set; } = TimeSpan.FromHours(1);
}
```

## Dependency Injection Configuration

```csharp
public static class CacheServiceCollectionExtensions
{
    public static IServiceCollection AddFhirCaching(this IServiceCollection services, IConfiguration configuration)
    {
        var cacheSection = configuration.GetSection("Cache");
        var cacheType = cacheSection.GetValue<string>("Type", "Memory");

        return cacheType.ToLowerInvariant() switch
        {
            "redis" => services.AddRedisCaching(cacheSection),
            "memory" => services.AddMemoryCaching(cacheSection),
            _ => throw new InvalidOperationException($"Unsupported cache type: {cacheType}")
        };
    }

    private static IServiceCollection AddMemoryCaching(this IServiceCollection services, IConfigurationSection config)
    {
        services.Configure<MemoryCacheOptions>(config.GetSection("Memory"));
        services.AddSingleton<ICache, MemoryEfficientCache>();
        services.AddSingleton<ITenantCache, TenantScopedMemoryCache>();
        services.AddScoped<IFhirCacheService, FhirCacheService>();
        return services;
    }

    private static IServiceCollection AddRedisCaching(this IServiceCollection services, IConfigurationSection config)
    {
        services.Configure<RedisCacheOptions>(config.GetSection("Redis"));

        var connectionString = config.GetConnectionString("Redis") ?? throw new InvalidOperationException("Redis connection string is required");
        services.AddSingleton<IConnectionMultiplexer>(provider => ConnectionMultiplexer.Connect(connectionString));

        services.AddSingleton<ISerializer, SystemTextJsonSerializer>();
        services.AddSingleton<ICache, RedisCache>();
        services.AddSingleton<ITenantCache, TenantScopedMemoryCache>(); // Use Redis-backed implementation
        services.AddScoped<IFhirCacheService, FhirCacheService>();

        return services;
    }
}
```

This caching architecture provides:

1. **Unified Interface**: Same API for memory and distributed caching
2. **Tenant Isolation**: Automatic key prefixing and cache scoping
3. **Memory Efficiency**: RecyclableMemoryStream and span-based operations
4. **Performance**: Bulk operations and efficient serialization
5. **Flexibility**: Easy switching between cache providers
6. **Observability**: Logging and statistics support

The design ensures that FHIR resources, search results, schema providers, and other data can be efficiently cached across web farm scenarios while maintaining tenant isolation and optimal performance.