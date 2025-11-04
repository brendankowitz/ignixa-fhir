// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ignixa.DataLayer.SqlEntityFramework.Entities;

namespace Ignixa.DataLayer.SqlEntityFramework.Indexing;

/// <summary>
/// Caches lookup IDs for search parameter indexing (SearchParamId, SystemId, QuantityCodeId, ResourceTypeId).
/// Provides thread-safe get-or-create operations for reference data.
/// </summary>
public class SearchIndexReferenceDataCache
{
    private readonly FhirDbContext _context;
    private readonly ILogger<SearchIndexReferenceDataCache> _logger;

    // Caches: Key -> ID
    private readonly ConcurrentDictionary<string, short> _searchParamCache = new();
    private readonly ConcurrentDictionary<string, int> _systemCache = new();
    private readonly ConcurrentDictionary<string, int> _quantityCodeCache = new();
    private readonly ConcurrentDictionary<string, short> _resourceTypeCache = new();

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
    /// Pre-loads all SearchParam entries into cache for better performance.
    /// Call this during initialization to avoid repeated database queries.
    /// </summary>
    public async Task PreloadSearchParamsAsync()
    {
        var searchParams = await _context.SearchParams
            .AsNoTracking()
            .ToListAsync();

        foreach (var sp in searchParams)
        {
            _searchParamCache.TryAdd(sp.Uri, sp.SearchParamId);
        }

        _logger.LogInformation("Preloaded {Count} search parameters into cache", searchParams.Count);
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
}
