// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.Search.Definition;
using Ignixa.Search.Models;

namespace Ignixa.DataLayer.SqlEntityFramework.Indexing;

/// <summary>
/// Caches lookup IDs for search parameter indexing (SearchParamId, SystemId, QuantityCodeId, ResourceTypeId).
/// Provides thread-safe get-or-create operations for reference data.
/// Uses on-demand caching for large datasets (Systems, QuantityCodes) to prevent memory exhaustion.
/// </summary>
public class SearchIndexReferenceDataCache : IDisposable
{
    private readonly FhirDbContext _context;
    private readonly ILogger<SearchIndexReferenceDataCache> _logger;
    private bool _disposed;

    // Caches: Key -> ID
    private readonly ConcurrentDictionary<string, short> _searchParamCache = new();
    private readonly ConcurrentDictionary<string, int> _systemCache = new();
    private readonly ConcurrentDictionary<string, int> _quantityCodeCache = new();
    private readonly ConcurrentDictionary<string, short> _resourceTypeCache = new();

    // Lazy-loading wrappers (initialized on-demand)
    private LazyLoadingDictionary<string, short>? _resourceTypeMappingsWrapper;
    private LazyLoadingDictionary<string, short>? _searchParameterMappingsWrapper;
    private LazyLoadingDictionary<string, int>? _systemMappingsWrapper;
    private LazyLoadingDictionary<string, int>? _quantityCodeMappingsWrapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchIndexReferenceDataCache"/> class.
    /// </summary>
    /// <param name="context">The EF Core DbContext.</param>
    /// <param name="logger">Logger instance.</param>
    public SearchIndexReferenceDataCache(
        FhirDbContext context,
        ILogger<SearchIndexReferenceDataCache> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the SearchParamId for a given search parameter URI.
    /// Returns null if the search parameter is not registered in the database.
    /// Caches both positive results (found) and negative results (not found) to avoid repeated database queries.
    /// </summary>
    /// <param name="uri">The search parameter URI (e.g., "http://hl7.org/fhir/SearchParameter/Patient-name").</param>
    /// <returns>The SearchParamId, or null if not found.</returns>
    public async ValueTask<short?> GetSearchParamIdAsync(string uri)
    {
        if (string.IsNullOrEmpty(uri))
        {
            return null;
        }

        // Check cache first (handles both found and not-found cases)
        if (_searchParamCache.TryGetValue(uri, out var cachedId))
        {
            // Sentinel value -1 means "not found" - return null without querying database
            return cachedId == -1 ? null : cachedId;
        }

        // Query database
        var entity = await _context.SearchParams
            .AsNoTracking()
            .FirstOrDefaultAsync(sp => sp.Uri == uri);

        if (entity == null)
        {
            _logger.LogWarning("SearchParam not found for URI: {Uri}", uri);
            // Cache the negative result using sentinel value -1 to avoid repeated database queries
            _searchParamCache.TryAdd(uri, -1);
            return null;
        }

        // Cache positive result and return
        _searchParamCache.TryAdd(uri, entity.SearchParamId);
        return entity.SearchParamId;
    }

    /// <summary>
    /// Gets the SearchParamId for a search parameter, with support for OverridesUrl fallback.
    /// If the search parameter URL is not found in the database, checks the OverridesUrl property
    /// to handle cases where Implementation Guide parameters override base FHIR parameters.
    /// </summary>
    /// <param name="searchParameter">The search parameter containing URL and optional OverridesUrl.</param>
    /// <returns>The SearchParamId, or null if not found (even after checking OverridesUrl).</returns>
    public async ValueTask<short?> GetSearchParamIdAsync(SearchParameterInfo searchParameter)
    {
        if (searchParameter?.Url == null)
        {
            return null;
        }

        // Try primary lookup using the parameter's URL
        var searchParamId = await GetSearchParamIdAsync(searchParameter.Url.ToString());
        if (searchParamId.HasValue)
        {
            return searchParamId;
        }

        // Fallback: if this parameter overrides another parameter, try the overridden URL
        if (searchParameter.OverridesUrl != null)
        {
            return await GetSearchParamIdAsync(searchParameter.OverridesUrl.ToString());
        }

        return null;
    }

    /// <summary>
    /// Gets or creates the SystemId for a given system URI.
    /// Creates a new entry if the system doesn't exist.
    /// </summary>
    /// <param name="systemUri">The system URI (e.g., "http://loinc.org").</param>
    /// <returns>The SystemId, or null if systemUri is null/empty.</returns>
    public async ValueTask<int?> GetOrCreateSystemIdAsync(string? systemUri)
    {
        if (string.IsNullOrEmpty(systemUri))
        {
            return null;
        }

        // Check cache first
        if (_systemCache.TryGetValue(systemUri, out var cachedId))
        {
            return cachedId;
        }

        // Query database
        var entity = await _context.Systems
            .FirstOrDefaultAsync(s => s.Value == systemUri);

        if (entity != null)
        {
            // Cache existing entry
            _systemCache.TryAdd(systemUri, entity.SystemId);
            return entity.SystemId;
        }

        // Create new entry
        var newEntity = new SystemEntity
        {
            Value = systemUri
        };

        _context.Systems.Add(newEntity);
        await _context.SaveChangesAsync();

        _logger.LogDebug("Created new System entry: {SystemUri} -> {SystemId}", systemUri, newEntity.SystemId);

        // Cache and return
        _systemCache.TryAdd(systemUri, newEntity.SystemId);
        return newEntity.SystemId;
    }

    /// <summary>
    /// Gets or creates the QuantityCodeId for a given unit code.
    /// Creates a new entry if the code doesn't exist.
    /// </summary>
    /// <param name="code">The unit code (e.g., "mg", "kg").</param>
    /// <returns>The QuantityCodeId, or null if code is null/empty.</returns>
    public async ValueTask<int?> GetOrCreateQuantityCodeIdAsync(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return null;
        }

        // Check cache first
        if (_quantityCodeCache.TryGetValue(code, out var cachedId))
        {
            return cachedId;
        }

        // Query database
        var entity = await _context.QuantityCodes
            .FirstOrDefaultAsync(qc => qc.Value == code);

        if (entity != null)
        {
            // Cache existing entry
            _quantityCodeCache.TryAdd(code, entity.QuantityCodeId);
            return entity.QuantityCodeId;
        }

        // Create new entry
        var newEntity = new QuantityCodeEntity
        {
            Value = code
        };

        _context.QuantityCodes.Add(newEntity);
        await _context.SaveChangesAsync();

        _logger.LogDebug("Created new QuantityCode entry: {Code} -> {QuantityCodeId}", code, newEntity.QuantityCodeId);

        // Cache and return
        _quantityCodeCache.TryAdd(code, newEntity.QuantityCodeId);
        return newEntity.QuantityCodeId;
    }

    /// <summary>
    /// Gets the ResourceTypeId for a given resource type name.
    /// Returns null if the resource type is not registered.
    /// Caches both positive results (found) and negative results (not found) to avoid repeated database queries.
    /// </summary>
    /// <param name="resourceTypeName">The resource type name (e.g., "Patient").</param>
    /// <returns>The ResourceTypeId, or null if not found.</returns>
    public async ValueTask<short?> GetResourceTypeIdAsync(string? resourceTypeName)
    {
        if (string.IsNullOrEmpty(resourceTypeName))
        {
            return null;
        }

        // Check cache first (handles both found and not-found cases)
        if (_resourceTypeCache.TryGetValue(resourceTypeName, out var cachedId))
        {
            // Sentinel value -1 means "not found" - return null without querying database
            return cachedId == -1 ? null : cachedId;
        }

        // Query database
        var entity = await _context.ResourceTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(rt => rt.Name == resourceTypeName);

        if (entity == null)
        {
            _logger.LogWarning("ResourceType not found: {ResourceTypeName}", resourceTypeName);
            // Cache the negative result using sentinel value -1 to avoid repeated database queries
            _resourceTypeCache.TryAdd(resourceTypeName, -1);
            return null;
        }

        // Cache positive result and return
        _resourceTypeCache.TryAdd(resourceTypeName, entity.ResourceTypeId);
        return entity.ResourceTypeId;
    }

    /// <summary>
    /// Pre-loads SearchParam entries into cache for better performance.
    /// Call this during initialization to avoid repeated database queries.
    /// SAFETY: Use maxRows parameter to limit memory usage for large datasets.
    /// For databases with 10K+ search parameters, rely on on-demand loading instead.
    /// </summary>
    /// <param name="maxRows">Optional maximum number of rows to load. Prevents memory exhaustion for large datasets.</param>
    public async Task PreloadSearchParamsAsync(int? maxRows = null)
    {
        var query = _context.SearchParams.AsNoTracking();

        if (maxRows.HasValue)
        {
            query = query.Take(maxRows.Value);
        }

        var searchParams = await query.ToListAsync();

        foreach (var sp in searchParams)
        {
            _searchParamCache.TryAdd(sp.Uri, sp.SearchParamId);
        }

        _logger.LogInformation(
            "Preloaded {Count} search parameters into cache{MaxRowsInfo}",
            searchParams.Count,
            maxRows.HasValue ? $" (limited to {maxRows.Value} rows)" : string.Empty);
    }

    /// <summary>
    /// Pre-loads all ResourceType entries into cache for better performance.
    /// Call this during initialization to avoid repeated database queries.
    /// </summary>
    public async Task PreloadResourceTypesAsync()
    {
        var resourceTypes = await _context.ResourceTypes
            .AsNoTracking()
            .ToListAsync();

        foreach (var rt in resourceTypes)
        {
            _resourceTypeCache.TryAdd(rt.Name, rt.ResourceTypeId);
        }

        _logger.LogInformation("Preloaded {Count} resource types into cache", resourceTypes.Count);
    }

    /// <summary>
    /// Synchronous lookup of SearchParamId from URI using in-memory cache only.
    /// Does NOT query the database. Returns 0 if not found in cache.
    /// Requires PreloadSearchParamsAsync to be called during initialization.
    /// </summary>
    /// <param name="uri">The search parameter URI.</param>
    /// <returns>The SearchParamId if found in cache, otherwise 0.</returns>
    public short TryGetSearchParamIdFromCache(string? uri)
    {
        if (string.IsNullOrEmpty(uri))
        {
            return 0;
        }

        // Check cache - only memory access, no database query
        if (_searchParamCache.TryGetValue(uri, out var cachedId))
        {
            // Sentinel value -1 means "not found" - return 0 (matches FHIR lenient behavior)
            return cachedId == -1 ? (short)0 : cachedId;
        }

        // Not in cache - return 0 (caller will handle lenient fallback)
        return 0;
    }

    /// <summary>
    /// Synchronous lookup of ResourceTypeId from name using in-memory cache only.
    /// Does NOT query the database. Returns null if not found in cache.
    /// Requires PreloadResourceTypesAsync to be called during initialization.
    /// </summary>
    /// <param name="resourceTypeName">The resource type name (e.g., "Patient").</param>
    /// <returns>The ResourceTypeId if found in cache, otherwise null.</returns>
    public short? TryGetResourceTypeIdFromCache(string? resourceTypeName)
    {
        if (string.IsNullOrEmpty(resourceTypeName))
        {
            return null;
        }

        // Check cache - only memory access, no database query
        if (_resourceTypeCache.TryGetValue(resourceTypeName, out var cachedId))
        {
            // Sentinel value -1 means "not found" - return null
            return cachedId == -1 ? null : cachedId;
        }

        // Not in cache - return null
        return null;
    }

    /// <summary>
    /// Synchronous reverse lookup of resource type name from ID using in-memory cache only.
    /// Does NOT query the database. Returns null if not found in cache.
    /// Requires PreloadResourceTypesAsync to be called during initialization.
    /// </summary>
    /// <param name="resourceTypeId">The resource type ID.</param>
    /// <returns>The resource type name if found in cache, otherwise null.</returns>
    public string? TryGetResourceTypeNameFromCache(short? resourceTypeId)
    {
        if (!resourceTypeId.HasValue || resourceTypeId.Value <= 0)
        {
            return null;
        }

        // Reverse lookup: iterate through cache to find the entry with this ID
        // This is O(n) but n is small (number of resource types is typically < 100)
        // and this is only called for RevInclude processing (not in main search path)
        foreach (var kvp in _resourceTypeCache)
        {
            // Skip sentinel values (negative IDs)
            if (kvp.Value == resourceTypeId.Value)
            {
                return kvp.Key;
            }
        }

        // Not in cache - return null
        return null;
    }

    /// <summary>
    /// Gets all resource type mappings with lazy-loading support.
    /// TryGetValue calls will automatically load missing entries from database.
    /// Filters out sentinel values (-1 for "not found" entries).
    /// Thread-safe and suitable for use in TVP row generators.
    /// </summary>
    public IReadOnlyDictionary<string, short> ResourceTypeMappings
    {
        get
        {
            if (_resourceTypeMappingsWrapper == null)
            {
                _resourceTypeMappingsWrapper = new LazyLoadingDictionary<string, short>(
                    _resourceTypeCache,
                    async key => await GetResourceTypeIdAsync(key) ?? -1,
                    _logger,
                    isValidValue: value => value > 0); // Filter out sentinel -1
            }

            return _resourceTypeMappingsWrapper;
        }
    }

    /// <summary>
    /// Gets all search parameter mappings with lazy-loading support.
    /// TryGetValue calls will automatically load missing entries from database.
    /// Filters out sentinel values (-1 for "not found" entries).
    /// Thread-safe and suitable for use in TVP row generators.
    /// </summary>
    public IReadOnlyDictionary<string, short> SearchParameterMappings
    {
        get
        {
            if (_searchParameterMappingsWrapper == null)
            {
                _searchParameterMappingsWrapper = new LazyLoadingDictionary<string, short>(
                    _searchParamCache,
                    async key => await GetSearchParamIdAsync(key) ?? -1,
                    _logger,
                    isValidValue: value => value > 0); // Filter out sentinel -1
            }

            return _searchParameterMappingsWrapper;
        }
    }

    /// <summary>
    /// Gets all system mappings with lazy-loading support.
    /// TryGetValue calls will automatically create missing entries in database.
    /// GetOrCreateSystemIdAsync ensures all values are valid (no sentinel values).
    /// Thread-safe and suitable for use in TVP row generators.
    /// </summary>
    public IReadOnlyDictionary<string, int> SystemMappings
    {
        get
        {
            if (_systemMappingsWrapper == null)
            {
                _systemMappingsWrapper = new LazyLoadingDictionary<string, int>(
                    _systemCache,
                    async key => await GetOrCreateSystemIdAsync(key) ?? 0,
                    _logger,
                    isValidValue: value => value > 0); // Filter out 0
            }

            return _systemMappingsWrapper;
        }
    }

    /// <summary>
    /// Gets all quantity code mappings with lazy-loading support.
    /// TryGetValue calls will automatically create missing entries in database.
    /// GetOrCreateQuantityCodeIdAsync ensures all values are valid (no sentinel values).
    /// Thread-safe and suitable for use in TVP row generators.
    /// </summary>
    public IReadOnlyDictionary<string, int> QuantityCodeMappings
    {
        get
        {
            if (_quantityCodeMappingsWrapper == null)
            {
                _quantityCodeMappingsWrapper = new LazyLoadingDictionary<string, int>(
                    _quantityCodeCache,
                    async key => await GetOrCreateQuantityCodeIdAsync(key) ?? 0,
                    _logger,
                    isValidValue: value => value > 0); // Filter out 0
            }

            return _quantityCodeMappingsWrapper;
        }
    }

    /// <summary>
    /// Gets valid resource type mappings (filters out sentinel values).
    /// Creates a NEW dictionary snapshot - use sparingly for operations that require sentinel filtering.
    /// For row generation or lookups that work with the live cache, use ResourceTypeMappings property directly.
    /// Row generators use TryGetValue which works correctly with sentinel values (cache miss vs. not found).
    /// </summary>
    public Dictionary<string, short> GetValidResourceTypeMappings()
    {
        return _resourceTypeCache
            .Where(kvp => kvp.Value > 0) // Filter out sentinel value -1
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>
    /// Gets valid search parameter mappings (filters out sentinel values).
    /// Creates a NEW dictionary snapshot - use sparingly for operations that require sentinel filtering.
    /// For row generation or lookups that work with the live cache, use SearchParameterMappings property directly.
    /// Row generators use TryGetValue which works correctly with sentinel values (cache miss vs. not found).
    /// </summary>
    public Dictionary<string, short> GetValidSearchParameterMappings()
    {
        return _searchParamCache
            .Where(kvp => kvp.Value > 0) // Filter out sentinel value -1
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>
    /// Syncs search parameters from in-memory manager to database.
    /// Used when packages (e.g., US Core) are loaded to ensure their search parameters
    /// are persisted to the SearchParam table for indexing pipeline.
    /// CRITICAL: Without this, package search parameters won't be found during row generation,
    /// causing "SearchParam URL not found" warnings and failed indexing.
    /// </summary>
    /// <param name="searchParameterUrls">List of search parameter canonical URLs to sync.</param>
    /// <param name="searchParamManager">Search parameter manager to check for OverridesUrl aliasing.</param>
    /// <returns>Number of search parameters synced to database.</returns>
    public async Task<int> SyncSearchParametersToDatabase(
        IEnumerable<string> searchParameterUrls,
        ISearchParameterDefinitionManager searchParamManager)
    {
        if (searchParameterUrls == null)
        {
            return 0;
        }

        var urls = searchParameterUrls.ToList();
        if (urls.Count == 0)
        {
            return 0;
        }

        _logger.LogInformation("Syncing {Count} search parameter URLs to database", urls.Count);

        var syncedCount = 0;

        foreach (var url in urls)
        {
            // Check if already exists in database
            var existing = await _context.SearchParams
                .AsNoTracking()
                .FirstOrDefaultAsync(sp => sp.Uri == url);

            if (existing != null)
            {
                // Already exists - update cache if needed
                _searchParamCache.TryAdd(url, existing.SearchParamId);
                _logger.LogDebug("Search parameter {Url} already exists in database with ID {SearchParamId}", url, existing.SearchParamId);
                continue;
            }

            // Get search parameter definition from manager to check for OverridesUrl
            SearchParameterInfo? paramInfo = null;
            if (searchParamManager != null && searchParamManager.TryGetSearchParameter(new Uri(url), out var param))
            {
                paramInfo = param;
            }

            short? searchParamIdToCache = null;

            // Check if this parameter overrides another one
            if (paramInfo?.OverridesUrl != null)
            {
                // Look up the overridden parameter's ID in the database
                var overriddenParam = await _context.SearchParams
                    .AsNoTracking()
                    .FirstOrDefaultAsync(sp => sp.Uri == paramInfo.OverridesUrl.ToString());

                if (overriddenParam != null)
                {
                    searchParamIdToCache = overriddenParam.SearchParamId;
                    _logger.LogInformation(
                        "Search parameter {Url} overrides {OverriddenUrl} - will use SearchParamId {SearchParamId} for indexing",
                        url,
                        paramInfo.OverridesUrl,
                        searchParamIdToCache);
                }
            }

            // Create new entry in database
            var newEntity = new Entities.SearchParamEntity
            {
                Uri = url,
                Status = "Enabled",
                LastUpdated = DateTimeOffset.UtcNow,
                IsPartiallySupported = false
            };

            _context.SearchParams.Add(newEntity);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Synced search parameter {Url} to database with ID {SearchParamId}", url, newEntity.SearchParamId);

            // Cache using the OVERRIDE ID if present, otherwise use the new ID
            var idToCache = searchParamIdToCache ?? newEntity.SearchParamId;
            _searchParamCache.TryAdd(url, idToCache);

            if (searchParamIdToCache.HasValue)
            {
                _logger.LogInformation(
                    "Cached search parameter {Url} with aliased SearchParamId {AliasedId} (own ID is {OwnId})",
                    url,
                    idToCache,
                    newEntity.SearchParamId);
            }

            syncedCount++;
        }

        _logger.LogInformation("Successfully synced {Count} search parameters to database", syncedCount);

        return syncedCount;
    }

    /// <summary>
    /// Gets cache statistics for monitoring and diagnostics.
    /// </summary>
    /// <returns>Cache statistics including counts of cached entries.</returns>
    public CacheStatistics GetStatistics()
    {
        return new CacheStatistics
        {
            SearchParamCount = _searchParamCache.Count(kvp => kvp.Value != -1),
            ResourceTypeCount = _resourceTypeCache.Count(kvp => kvp.Value != -1),
            SystemCount = _systemCache.Count,
            QuantityCodeCount = _quantityCodeCache.Count
        };
    }

    /// <summary>
    /// Dictionary wrapper that lazy-loads missing values from database synchronously.
    /// Used for bulk operations (TVP generation) where async/await is not available.
    /// Intercepts TryGetValue calls and loads values on cache miss using blocking async.
    /// </summary>
    /// <typeparam name="TKey">The dictionary key type.</typeparam>
    /// <typeparam name="TValue">The dictionary value type.</typeparam>
    private class LazyLoadingDictionary<TKey, TValue> : IReadOnlyDictionary<TKey, TValue>
        where TKey : notnull
    {
        private readonly ConcurrentDictionary<TKey, TValue> _cache;
        private readonly Func<TKey, Task<TValue?>> _loadFunc;
        private readonly ILogger _logger;
        private readonly Func<TValue?, bool>? _isValidValue;

        /// <summary>
        /// Initializes a new instance of the <see cref="LazyLoadingDictionary{TKey, TValue}"/> class.
        /// </summary>
        /// <param name="cache">The underlying cache dictionary.</param>
        /// <param name="loadFunc">Function to load missing values from database.</param>
        /// <param name="logger">Logger instance.</param>
        /// <param name="isValidValue">Optional function to validate loaded values (e.g., filter sentinel values).</param>
        public LazyLoadingDictionary(
            ConcurrentDictionary<TKey, TValue> cache,
            Func<TKey, Task<TValue?>> loadFunc,
            ILogger logger,
            Func<TValue?, bool>? isValidValue = null)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _loadFunc = loadFunc ?? throw new ArgumentNullException(nameof(loadFunc));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _isValidValue = isValidValue;
        }

        /// <summary>
        /// Attempts to get the value for the specified key, lazy-loading from database if not in cache.
        /// Uses blocking async call to load missing values synchronously.
        /// </summary>
        /// <param name="key">The key to look up.</param>
        /// <param name="value">The value if found.</param>
        /// <returns>True if value was found or loaded successfully, false otherwise.</returns>
        public bool TryGetValue(TKey key, out TValue value)
        {
            // Check cache first
            if (_cache.TryGetValue(key, out value!))
            {
                // If we have a validation function, check if the cached value is valid
                if (_isValidValue != null && !_isValidValue(value))
                {
                    // Invalid value (e.g., sentinel -1) - return false
                    value = default!;
                    return false;
                }

                return true;
            }

            // Cache miss - lazy load from database (blocking async call)
            _logger.LogDebug("Cache miss for {Key} - lazy loading from database", key);

            try
            {
                var loadedValue = _loadFunc(key).GetAwaiter().GetResult();

                // Check if loaded value is valid
                if (_isValidValue != null && !_isValidValue(loadedValue))
                {
                    // Loaded value is invalid (e.g., null or sentinel) - cache it but return false
                    if (loadedValue != null && !EqualityComparer<TValue>.Default.Equals(loadedValue, default))
                    {
                        _cache.TryAdd(key, loadedValue);
                    }

                    value = default!;
                    return false;
                }

                // Valid value loaded
                if (loadedValue != null && !EqualityComparer<TValue>.Default.Equals(loadedValue, default))
                {
                    value = loadedValue;
                    _cache.TryAdd(key, value); // Update cache
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to lazy load value for key {Key}", key);
            }

            value = default!;
            return false;
        }

        // IReadOnlyDictionary implementation - delegates to underlying cache
        public IEnumerable<TKey> Keys => _cache.Keys;
        public IEnumerable<TValue> Values => _cache.Values;
        public int Count => _cache.Count;
        public bool ContainsKey(TKey key) => _cache.ContainsKey(key);

        public TValue this[TKey key]
        {
            get
            {
                if (TryGetValue(key, out var value))
                {
                    return value;
                }

                throw new KeyNotFoundException($"The given key '{key}' was not present in the dictionary.");
            }
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _cache.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Disposes the cache and releases the underlying DbContext.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected implementation of Dispose pattern.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if called from finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed resources
                _context?.Dispose();
            }

            // No unmanaged resources to release

            _disposed = true;
        }
    }
}
