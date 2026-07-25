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
/// CRITICAL: Uses SemaphoreSlim to ensure thread-safe database access since DbContext is not thread-safe.
/// </summary>
public class SearchIndexReferenceDataCache : IDisposable
{
    private readonly FhirDbContext _context;
    private readonly ILogger<SearchIndexReferenceDataCache> _logger;
    private readonly SemaphoreSlim _dbLock = new(1, 1); // Ensures only one database operation at a time
    private bool _disposed;

    // Caches: Key -> ID
    private readonly ConcurrentDictionary<string, short> _searchParamCache = new();
    private readonly ConcurrentDictionary<string, int> _systemCache = new();
    private readonly ConcurrentDictionary<string, int> _quantityCodeCache = new();
    private readonly ConcurrentDictionary<string, short> _resourceTypeCache = new();

    // Negative caches for the read-only lookups. Kept separate from the positive caches above because
    // those are shared with the get-or-create write path, which reads every cached integer as a real ID.
    private readonly NegativeLookupCache _missingSystems;
    private readonly NegativeLookupCache _missingQuantityCodes;

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
    /// <param name="timeProvider">Clock backing the negative caches' TTL. Defaults to <see cref="TimeProvider.System"/>.</param>
    public SearchIndexReferenceDataCache(
        FhirDbContext context,
        ILogger<SearchIndexReferenceDataCache> logger,
        TimeProvider? timeProvider = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _missingSystems = new NegativeLookupCache(timeProvider);
        _missingQuantityCodes = new NegativeLookupCache(timeProvider);
    }

    /// <summary>
    /// Initializes the cache by batch-loading all search parameters from the database.
    /// This prevents N+1 query problems during startup when search parameters are being synced.
    /// Call this method once after creating the cache instance.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            var searchParams = await _context.SearchParams
                .AsNoTracking()
                .Select(sp => new { sp.Uri, sp.SearchParamId })
                .ToListAsync(cancellationToken);

            foreach (var sp in searchParams)
            {
                _searchParamCache[sp.Uri] = sp.SearchParamId;
            }

            _logger.LogInformation("Initialized SearchIndexReferenceDataCache with {Count} search parameters", searchParams.Count);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Gets the SearchParamId for a given search parameter URI.
    /// Returns null if the search parameter is not registered in the database.
    /// Caches both positive results (found) and negative results (not found) to avoid repeated database queries.
    /// Thread-safe: Uses semaphore to ensure single database access at a time.
    /// </summary>
    /// <param name="uri">The search parameter URI (e.g., "http://hl7.org/fhir/SearchParameter/Patient-name").</param>
    /// <param name="cancellationToken">Observed before the cache is consulted, then cancels the lock wait and the database round trip.</param>
    /// <returns>The SearchParamId, or null if not found.</returns>
    public async ValueTask<short?> GetSearchParamIdAsync(string uri, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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

        // Acquire lock for database access (DbContext is not thread-safe)
        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check cache after acquiring lock (another thread may have loaded it)
            if (_searchParamCache.TryGetValue(uri, out cachedId))
            {
                return cachedId == -1 ? null : cachedId;
            }

            // Query database
            var entity = await _context.SearchParams
                .AsNoTracking()
                .FirstOrDefaultAsync(sp => sp.Uri == uri, cancellationToken);

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
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Gets the SearchParamId for a search parameter, with support for OverridesUrl fallback.
    /// If the search parameter URL is not found in the database, checks the OverridesUrl property
    /// to handle cases where Implementation Guide parameters override base FHIR parameters.
    /// </summary>
    /// <param name="searchParameter">The search parameter containing URL and optional OverridesUrl.</param>
    /// <param name="cancellationToken">Token that cancels the lock wait and the database round trip.</param>
    /// <returns>The SearchParamId, or null if not found (even after checking OverridesUrl).</returns>
    public async ValueTask<short?> GetSearchParamIdAsync(SearchParameterInfo searchParameter, CancellationToken cancellationToken = default)
    {
        if (searchParameter?.Url == null)
        {
            return null;
        }

        // Try primary lookup using the parameter's URL
        var searchParamId = await GetSearchParamIdAsync(searchParameter.Url.ToString(), cancellationToken);
        if (searchParamId.HasValue)
        {
            return searchParamId;
        }

        // Fallback: if this parameter overrides another parameter, try the overridden URL
        if (searchParameter.OverridesUrl != null)
        {
            return await GetSearchParamIdAsync(searchParameter.OverridesUrl.ToString(), cancellationToken);
        }

        return null;
    }

    /// <summary>
    /// Gets or creates the SystemId for a given system URI.
    /// Creates a new entry if the system doesn't exist.
    /// Thread-safe: Uses semaphore to ensure single database access at a time.
    /// </summary>
    /// <param name="systemUri">The system URI (e.g., "http://loinc.org").</param>
    /// <param name="cancellationToken">Observed before the cache is consulted, then cancels the lock wait and the database round trip.</param>
    /// <returns>The SystemId, or null if systemUri is null/empty.</returns>
    public async ValueTask<int?> GetOrCreateSystemIdAsync(string? systemUri, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(systemUri))
        {
            return null;
        }

        // Check cache first
        if (_systemCache.TryGetValue(systemUri, out var cachedId))
        {
            return cachedId;
        }

        // Acquire lock for database access (DbContext is not thread-safe)
        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check cache after acquiring lock
            if (_systemCache.TryGetValue(systemUri, out cachedId))
            {
                return cachedId;
            }

            // Query database
            var entity = await _context.Systems
                .FirstOrDefaultAsync(s => s.Value == systemUri, cancellationToken);

            if (entity != null)
            {
                // Cache existing entry
                _systemCache.TryAdd(systemUri, entity.SystemId);
                _missingSystems.Forget(systemUri);
                return entity.SystemId;
            }

            // Create new entry
            var newEntity = new SystemEntity
            {
                Value = systemUri
            };

            await SaveNewEntityAsync(newEntity, cancellationToken);

            _logger.LogDebug("Created new System entry: {SystemUri} -> {SystemId}", systemUri, newEntity.SystemId);

            // Cache and return. Forgetting the negative entry is what stops a search that already
            // recorded this system as missing from continuing to report it missing now that it exists.
            _systemCache.TryAdd(systemUri, newEntity.SystemId);
            _missingSystems.Forget(systemUri);
            return newEntity.SystemId;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Gets or creates the QuantityCodeId for a given unit code.
    /// Creates a new entry if the code doesn't exist.
    /// Thread-safe: Uses semaphore to ensure single database access at a time.
    /// </summary>
    /// <param name="code">The unit code (e.g., "mg", "kg").</param>
    /// <param name="cancellationToken">Observed before the cache is consulted, then cancels the lock wait and the database round trip.</param>
    /// <returns>The QuantityCodeId, or null if code is null/empty.</returns>
    public async ValueTask<int?> GetOrCreateQuantityCodeIdAsync(string? code, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(code))
        {
            return null;
        }

        // Check cache first
        if (_quantityCodeCache.TryGetValue(code, out var cachedId))
        {
            return cachedId;
        }

        // Acquire lock for database access (DbContext is not thread-safe)
        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check cache after acquiring lock
            if (_quantityCodeCache.TryGetValue(code, out cachedId))
            {
                return cachedId;
            }

            // Query database
            var entity = await _context.QuantityCodes
                .FirstOrDefaultAsync(qc => qc.Value == code, cancellationToken);

            if (entity != null)
            {
                // Cache existing entry
                _quantityCodeCache.TryAdd(code, entity.QuantityCodeId);
                _missingQuantityCodes.Forget(code);
                return entity.QuantityCodeId;
            }

            // Create new entry
            var newEntity = new QuantityCodeEntity
            {
                Value = code
            };

            await SaveNewEntityAsync(newEntity, cancellationToken);

            _logger.LogDebug("Created new QuantityCode entry: {Code} -> {QuantityCodeId}", code, newEntity.QuantityCodeId);

            // Cache and return. Forgetting the negative entry is what stops a search that already
            // recorded this code as missing from continuing to report it missing now that it exists.
            _quantityCodeCache.TryAdd(code, newEntity.QuantityCodeId);
            _missingQuantityCodes.Forget(code);
            return newEntity.QuantityCodeId;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Stages <paramref name="entity"/> as a new row and saves it, leaving the change tracker clean of it
    /// whether the save succeeds or not.
    /// </summary>
    /// <remarks>
    /// The <see cref="FhirDbContext"/> here lives for the whole process -- <see cref="MultiTenantSearchIndexCache"/>
    /// holds the owning cache as a singleton -- so an entity abandoned in <see cref="EntityState.Added"/> is not
    /// scoped to the caller that abandoned it. The next unrelated <c>SaveChangesAsync</c> would re-attempt the
    /// insert and surface the unique-constraint violation against the wrong request. Cancellation is the ordinary
    /// way in, and the token is still passed down because the round trip must stay cancellable; the cleanup
    /// belongs here rather than in the token.
    /// </remarks>
    private async Task SaveNewEntityAsync<TEntity>(TEntity entity, CancellationToken cancellationToken)
        where TEntity : class
    {
        var entry = _context.Entry(entity);
        entry.State = EntityState.Added;

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            // Also on success: the store-generated key is materialized on the instance by now, so the
            // tracked entry has no further use, and leaving it behind would accumulate one entry per row
            // ever created here for the life of the process -- which every later DetectChanges walks.
            entry.State = EntityState.Detached;
        }
    }

    /// <summary>
    /// Looks up an existing SystemId for the given system URI without creating a new row.
    /// Returns null when <paramref name="systemUri"/> is null/empty or has no matching row.
    /// Caches only positive (found) results in <c>_systemCache</c>: that cache is also used by
    /// <see cref="GetOrCreateSystemIdAsync"/>, which treats every cached integer as a real ID, so
    /// caching a sentinel would corrupt the write path. Misses go to the separate
    /// <see cref="NegativeLookupCache"/>, consulted before the lock so a search naming unindexed
    /// terminology does not serialize behind ingest on every occurrence.
    /// Thread-safe: uses <c>_dbLock</c> for database access.
    /// </summary>
    /// <param name="systemUri">The system URI to look up (e.g., "http://loinc.org").</param>
    /// <param name="cancellationToken">
    /// Observed before either cache is consulted, then cancels the lock wait and the database round trip.
    /// Checking it first is load-bearing: a null return is compiled into <c>Predicate.False</c>, so a cancelled
    /// search answered from the negative cache would be reported as "this terminology does not exist".
    /// </param>
    /// <returns>The SystemId if found; null otherwise.</returns>
    public async ValueTask<int?> GetSystemIdAsync(string? systemUri, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(systemUri))
        {
            return null;
        }

        // Check cache first (only positive results are stored here)
        if (_systemCache.TryGetValue(systemUri, out var cachedId))
        {
            return cachedId;
        }

        if (_missingSystems.IsKnownMissing(systemUri))
        {
            return null;
        }

        // Acquire lock for database access (DbContext is not thread-safe)
        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check cache after acquiring lock
            if (_systemCache.TryGetValue(systemUri, out cachedId))
            {
                return cachedId;
            }

            // Read-only query: no tracking, no entity creation, no SaveChangesAsync
            var entity = await _context.Systems
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Value == systemUri, cancellationToken);

            if (entity == null)
            {
                _logger.LogDebug("System not found: {SystemUri}", systemUri);
                _missingSystems.RecordMiss(systemUri);
                return null;
            }

            // Cache positive result only -- misses are not cached to avoid corrupting the write path
            _systemCache.TryAdd(systemUri, entity.SystemId);
            return entity.SystemId;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Looks up existing SystemIds for a set of system URIs in a single database round trip
    /// (<c>WHERE Value IN (...)</c>), taking <c>_dbLock</c> at most once for the whole set.
    /// Every requested URI appears in the result, mapped to null when it has no row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Same caching contract as <see cref="GetSystemIdAsync"/>: positive results land in the shared
    /// <c>_systemCache</c>, misses in the separate negative cache. Keys already answerable from either
    /// cache are excluded from the query, so a warm cache issues no round trip at all.
    /// </para>
    /// <para>
    /// A returned row is credited only to the requested spelling that equals its stored <c>Value</c>
    /// ordinally. A requested spelling that differs only by case from a returned row is a question about the
    /// column's collation, which this method cannot read, so it re-queries that exact spelling and credits it
    /// only if the database confirms the match. Crediting it unconditionally would be a wrong positive under a
    /// case-sensitive collation and would poison the ordinal <c>_systemCache</c> for the process lifetime;
    /// recording it as a miss would be wrong under a case-insensitive one and would disagree with
    /// <see cref="GetSystemIdAsync"/>. Deferring to the database keeps the two paths in agreement under either
    /// collation.
    /// </para>
    /// </remarks>
    /// <param name="systemUris">The system URIs to look up.</param>
    /// <param name="cancellationToken">Observed before either cache is consulted, then cancels the lock wait and the database round trip.</param>
    /// <returns>A map from every requested URI to its SystemId, or null where no row exists.</returns>
    public async Task<IReadOnlyDictionary<string, int?>> GetSystemIdsAsync(
        IReadOnlyCollection<string> systemUris,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(systemUris);
        cancellationToken.ThrowIfCancellationRequested();

        var results = new Dictionary<string, int?>(StringComparer.Ordinal);
        var pending = new List<string>();

        foreach (var systemUri in systemUris)
        {
            if (string.IsNullOrEmpty(systemUri) || results.ContainsKey(systemUri))
            {
                continue;
            }

            if (_systemCache.TryGetValue(systemUri, out var cachedId))
            {
                results[systemUri] = cachedId;
            }
            else if (_missingSystems.IsKnownMissing(systemUri))
            {
                results[systemUri] = null;
            }
            else
            {
                results[systemUri] = null;
                pending.Add(systemUri);
            }
        }

        if (pending.Count == 0)
        {
            return results;
        }

        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            var found = await _context.Systems
                .AsNoTracking()
                .Where(s => pending.Contains(s.Value))
                .Select(s => new { s.Value, s.SystemId })
                .ToListAsync(cancellationToken);

            var foundByValue = found.ToDictionary(entry => entry.Value, entry => entry.SystemId, StringComparer.Ordinal);
            var foundIgnoringCase = new HashSet<string>(found.Select(entry => entry.Value), StringComparer.OrdinalIgnoreCase);

            foreach (var systemUri in pending)
            {
                // An ordinal-equal row is this spelling under any collation.
                if (foundByValue.TryGetValue(systemUri, out var systemId))
                {
                    _systemCache.TryAdd(systemUri, systemId);
                    results[systemUri] = systemId;
                    continue;
                }

                // A row came back that differs only by case. Whether it answers to THIS spelling is a
                // question about the column's collation, which this code cannot read: crediting it is
                // wrong under a case-sensitive one and recording a miss is wrong under a case-insensitive
                // one, and either answer would also disagree with GetSystemIdAsync. Ask the database about
                // the exact spelling instead -- the same equality it would apply, under its own collation.
                if (foundIgnoringCase.Contains(systemUri))
                {
                    var exactMatch = await _context.Systems
                        .AsNoTracking()
                        .Where(s => s.Value == systemUri)
                        .Select(s => (int?)s.SystemId)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (exactMatch is { } exactId)
                    {
                        _systemCache.TryAdd(systemUri, exactId);
                        results[systemUri] = exactId;
                        continue;
                    }
                }

                _logger.LogDebug("System not found: {SystemUri}", systemUri);
                _missingSystems.RecordMiss(systemUri);
            }

            return results;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Looks up an existing QuantityCodeId for the given unit code without creating a new row.
    /// Returns null when <paramref name="code"/> is null/empty or has no matching row.
    /// Caches only positive (found) results in <c>_quantityCodeCache</c>: that cache is also used by
    /// <see cref="GetOrCreateQuantityCodeIdAsync"/>, which treats every cached integer as a real ID,
    /// so caching a sentinel would corrupt the write path. Misses go to the separate
    /// <see cref="NegativeLookupCache"/>, consulted before the lock so a search naming unindexed
    /// terminology does not serialize behind ingest on every occurrence.
    /// Thread-safe: uses <c>_dbLock</c> for database access.
    /// </summary>
    /// <param name="code">The unit code to look up (e.g., "mg").</param>
    /// <param name="cancellationToken">
    /// Observed before either cache is consulted, then cancels the lock wait and the database round trip.
    /// See <see cref="GetSystemIdAsync"/> for why the pre-cache check matters.
    /// </param>
    /// <returns>The QuantityCodeId if found; null otherwise.</returns>
    public async ValueTask<int?> GetQuantityCodeIdAsync(string? code, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(code))
        {
            return null;
        }

        // Check cache first (only positive results are stored here)
        if (_quantityCodeCache.TryGetValue(code, out var cachedId))
        {
            return cachedId;
        }

        if (_missingQuantityCodes.IsKnownMissing(code))
        {
            return null;
        }

        // Acquire lock for database access (DbContext is not thread-safe)
        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check cache after acquiring lock
            if (_quantityCodeCache.TryGetValue(code, out cachedId))
            {
                return cachedId;
            }

            // Read-only query: no tracking, no entity creation, no SaveChangesAsync
            var entity = await _context.QuantityCodes
                .AsNoTracking()
                .FirstOrDefaultAsync(qc => qc.Value == code, cancellationToken);

            if (entity == null)
            {
                _logger.LogDebug("QuantityCode not found: {Code}", code);
                _missingQuantityCodes.RecordMiss(code);
                return null;
            }

            // Cache positive result only -- misses are not cached to avoid corrupting the write path
            _quantityCodeCache.TryAdd(code, entity.QuantityCodeId);
            return entity.QuantityCodeId;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Gets the ResourceTypeId for a given resource type name.
    /// Returns null if the resource type is not registered.
    /// Caches both positive results (found) and negative results (not found) to avoid repeated database queries.
    /// Thread-safe: Uses semaphore to ensure single database access at a time.
    /// </summary>
    /// <param name="resourceTypeName">The resource type name (e.g., "Patient").</param>
    /// <param name="cancellationToken">Observed before the cache is consulted, then cancels the lock wait and the database round trip.</param>
    /// <returns>The ResourceTypeId, or null if not found.</returns>
    public async ValueTask<short?> GetResourceTypeIdAsync(string? resourceTypeName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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

        // Acquire lock for database access (DbContext is not thread-safe)
        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check cache after acquiring lock
            if (_resourceTypeCache.TryGetValue(resourceTypeName, out cachedId))
            {
                return cachedId == -1 ? null : cachedId;
            }

            // Query database
            var entity = await _context.ResourceTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(rt => rt.Name == resourceTypeName, cancellationToken);

            if (entity == null)
            {
                // Deliberately NOT cached. dbo.ResourceType is populated as types are first
                // encountered, so "absent" is a transient state, not a stable fact. Caching the
                // miss for the process lifetime permanently poisons every later write of that
                // type -- the row generators drop the resource and the write fails or, worse,
                // silently loses a bundle entry. A repeated indexed lookup on a rare miss is the
                // cheaper trade.
                _logger.LogWarning("ResourceType not found: {ResourceTypeName}", resourceTypeName);
                return null;
            }

            // Cache positive result and return
            _resourceTypeCache.TryAdd(resourceTypeName, entity.ResourceTypeId);
            return entity.ResourceTypeId;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Pre-loads SearchParam entries into cache for better performance.
    /// Call this during initialization to avoid repeated database queries.
    /// SAFETY: Use maxRows parameter to limit memory usage for large datasets.
    /// For databases with 10K+ search parameters, rely on on-demand loading instead.
    /// Thread-safe: Uses semaphore to ensure single database access at a time.
    /// </summary>
    /// <param name="maxRows">Optional maximum number of rows to load. Prevents memory exhaustion for large datasets.</param>
    public async Task PreloadSearchParamsAsync(int? maxRows = null)
    {
        await _dbLock.WaitAsync();
        try
        {
            var query = _context.SearchParams.AsNoTracking();

            if (maxRows.HasValue)
            {
                query = query.Take(maxRows.Value);
            }

            var searchParams = await query.ToListAsync();

            foreach (var sp in searchParams)
            {
                _searchParamCache[sp.Uri] = sp.SearchParamId;
            }

            _logger.LogInformation(
                "Preloaded {Count} search parameters into cache{MaxRowsInfo}",
                searchParams.Count,
                maxRows.HasValue ? $" (limited to {maxRows.Value} rows)" : string.Empty);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Pre-loads all ResourceType entries into cache for better performance.
    /// Call this during initialization to avoid repeated database queries.
    /// Thread-safe: Uses semaphore to ensure single database access at a time.
    /// </summary>
    public async Task PreloadResourceTypesAsync()
    {
        await _dbLock.WaitAsync();
        try
        {
            var resourceTypes = await _context.ResourceTypes
                .AsNoTracking()
                .ToListAsync();

            foreach (var rt in resourceTypes)
            {
                _resourceTypeCache[rt.Name] = rt.ResourceTypeId;
            }

            _logger.LogInformation("Preloaded {Count} resource types into cache", resourceTypes.Count);
        }
        finally
        {
            _dbLock.Release();
        }
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
    /// Thread-safe: Uses semaphore to ensure single database access at a time.
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

        await _dbLock.WaitAsync();
        try
        {
            var syncedCount = 0;
            var existingList = await _context.SearchParams
                .AsNoTracking()
                .ToListAsync();

            foreach (var url in urls)
            {
                // Check if already exists in database

                var existing = existingList.FirstOrDefault(sp => sp.Uri == url);

                if (existing != null)
                {
                    // Overwrite rather than TryAdd: a lookup that ran before this sync may have cached the
                    // -1 "not found" sentinel for this URL, and TryAdd would leave it there for the process
                    // lifetime -- every resource would then index with this parameter's rows silently dropped.
                    _searchParamCache[url] = existing.SearchParamId;
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

                await SaveNewEntityAsync(newEntity, CancellationToken.None);

                _logger.LogInformation("Synced search parameter {Url} to database with ID {SearchParamId}", url, newEntity.SearchParamId);

                // Cache using the OVERRIDE ID if present, otherwise use the new ID. Overwrite rather than
                // TryAdd, for the same sentinel reason as the existing-row branch above.
                var idToCache = searchParamIdToCache ?? newEntity.SearchParamId;
                _searchParamCache[url] = idToCache;

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
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Drops any recorded "this system is missing" answer for <paramref name="systemUri"/>.
    /// </summary>
    /// <remarks>
    /// For writers that create <c>dbo.System</c> rows through their own <see cref="FhirDbContext"/> rather than
    /// through <see cref="GetOrCreateSystemIdAsync"/> -- CodeSystem import being the one that matters, since
    /// making unknown terminology known is its entire purpose. Without this, a search that probed the system
    /// beforehand keeps answering "missing" until the negative entry expires.
    /// </remarks>
    /// <param name="systemUri">The system URI whose recorded miss should be discarded.</param>
    public void ForgetMissingSystem(string? systemUri)
    {
        if (!string.IsNullOrEmpty(systemUri))
        {
            _missingSystems.Forget(systemUri);
        }
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
        /// Returns false only for "no such row"; a failed load throws rather than masquerading as an
        /// absent key, because callers turn an absent key into an unindexed search-parameter row --
        /// a silent data-loss outcome that a transient database error must never produce.
        /// </summary>
        /// <remarks>
        /// The load is sync-over-async because <see cref="IReadOnlyDictionary{TKey, TValue}"/> fixes this
        /// signature and the TVP row generators calling it are synchronous. Blocking on it is safe under the
        /// hosting models this assembly runs in -- ASP.NET Core and the generic host install no
        /// <see cref="SynchronizationContext"/>, so the continuation resumes on a thread-pool thread rather
        /// than waiting for the one blocked here. It is deliberately not wrapped in
        /// <see cref="Task.Run{TResult}(Func{Task{TResult}})"/>: that wrapper removed no deadlock (there is no
        /// context to capture) while occupying a second pool thread per lookup during bulk TVP generation.
        /// A host that does install a synchronization context would need an async row-generator interface, not
        /// a wrapper here.
        /// </remarks>
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

                // Invalid means "no such row" (the load func maps that to a sentinel). Not cached:
                // reference-data rows are created on demand, so absence is transient and caching
                // it would keep reporting the key as missing long after the row exists.
                if (_isValidValue != null && !_isValidValue(loadedValue))
                {
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
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to lazy load reference-data value for key '{key}'. Reporting the key as absent would silently drop a search-index row.",
                    ex);
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
                _dbLock?.Dispose();
            }

            // No unmanaged resources to release

            _disposed = true;
        }
    }
}
