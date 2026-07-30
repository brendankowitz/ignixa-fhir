using System.Collections;
using System.Collections.Concurrent;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer.Indexing;

/// <summary>
/// ADO.NET port of the write-path surface of <c>SearchIndexReferenceDataCache</c>
/// (Ignixa.DataLayer.SqlEntityFramework.Indexing). Only the 8 members the write path actually calls
/// are ported here -- read-path-only members (SyncSearchParametersToDatabase, GetStatistics, the
/// SearchParameterInfo overload of GetSearchParamIdAsync, GetValidResourceTypeMappings,
/// GetValidSearchParameterMappings) are intentionally not ported (YAGNI); Phase E's read-path cache
/// port will add those separately if it needs them.
/// <para>
/// The read-only lookups added since (TryGetSystemIdAsync, TryGetQuantityCodeIdAsync) record their misses
/// in a separate <see cref="NegativeLookupCache"/> rather than as a sentinel in the positive caches, and
/// so also port EF's ForgetMissingSystem as <see cref="ForgetMissingSystem"/> plus a quantity-code
/// equivalent. What is NOT ported is EF's cross-tenant broadcast (MultiTenantSearchIndexCache): these
/// hooks reach one cache instance only.
/// </para>
/// </summary>
public sealed class SqlServerSearchIndexReferenceDataCache(
    ISqlExecutionService sqlExecutionService,
    int tenantId,
    ILogger<SqlServerSearchIndexReferenceDataCache> logger,
    TimeProvider? timeProvider = null) : IDisposable
{
    private const short MissingSentinel = -1;

    /// <summary>
    /// Value meaning "not a real surrogate id" for <see cref="OnDemandResolvingDictionary{TKey,TValue}"/>,
    /// whose backing dictionary it cannot itself guarantee is sentinel-free. The system and quantity-code
    /// caches this class owns never hold it -- their misses go to <see cref="_missingSystems"/> and
    /// <see cref="_missingQuantityCodes"/>.
    /// </summary>
    private const int SystemQuantityMissingSentinel = -1;

    private readonly ISqlExecutionService _sqlExecutionService =
        sqlExecutionService ?? throw new ArgumentNullException(nameof(sqlExecutionService));
    private readonly ILogger<SqlServerSearchIndexReferenceDataCache> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    // "DbContext is not thread-safe" doesn't literally apply here (ISqlExecutionService opens a
    // fresh connection per call), but the double-check-locking pattern around the caches below is
    // still the right shape to avoid duplicate concurrent inserts within one process -- mirrors the
    // original's _dbLock exactly.
    private readonly SemaphoreSlim _dbLock = new(1, 1);

    private readonly ConcurrentDictionary<string, short> _resourceTypeCache = new();
    private readonly ConcurrentDictionary<string, short> _searchParamCache = new();
    private readonly ConcurrentDictionary<string, int> _systemCache = new();
    private readonly ConcurrentDictionary<string, int> _quantityCodeCache = new();

    // Negative caches for the read-only lookups, matching the key kinds the reference EF implementation
    // covers (_missingSystems and _missingQuantityCodes there too). Kept separate from the positive caches
    // above because those are shared with the get-or-create write path, which reads every cached integer as
    // a real surrogate id. Unlike a sentinel in the positive cache, these are TTL-bounded (so a row created
    // by another process becomes visible without any in-process invalidation) and capacity-bounded (so a
    // caller enumerating distinct systems cannot grow them without limit).
    private readonly NegativeLookupCache _missingSystems = new(timeProvider);
    private readonly NegativeLookupCache _missingQuantityCodes = new(timeProvider);

    // Completion signals for Ensure*PreloadedAsync's double-checked locking. Dictionary emptiness is
    // NOT a valid completion signal: ConcurrentDictionary is live-visible to readers DURING its own
    // population loop below, so a fast-path IsEmpty check can observe a partially-populated map mid-load
    // (the exact race this pair of flags exists to close). Only ever set to true from within _dbLock,
    // after the corresponding Load*Async call has fully completed.
    private volatile bool _resourceTypesLoaded;
    private volatile bool _searchParametersLoaded;

    // Test-only synchronization hook, invoked (if set) immediately after each row is inserted into
    // _searchParamCache during LoadSearchParamsAsync's population loop. Always null in production --
    // exists solely so a test can deterministically pause the loop mid-population and prove a
    // concurrent Ensure*PreloadedAsync caller correctly blocks instead of observing a partial map.
    internal Func<Task>? TestSearchParamRowInsertedHookAsync { get; set; }

    public IReadOnlyDictionary<string, short> ResourceTypeMappings => new SentinelFilteringDictionary(_resourceTypeCache);

    public IReadOnlyDictionary<string, short> SearchParameterMappings => new SentinelFilteringDictionary(_searchParamCache);

    // Self-healing: a miss resolves on demand via GetOrCreateSystemIdAsync/GetOrCreateQuantityCodeIdAsync
    // and is cached back into _systemCache/_quantityCodeCache -- which also forgets any miss the read path
    // recorded for that key. Each property access allocates a new
    // wrapper, but it always wraps the SAME shared, live backing ConcurrentDictionary by reference --
    // not a snapshot -- so inserts from any wrapper instance are immediately visible everywhere,
    // matching SqlServerMergeRepository's expectation that these mappings stay live.
    public IReadOnlyDictionary<string, int> SystemMappings =>
        new OnDemandResolvingDictionary<string, int>(_systemCache, GetOrCreateSystemIdAsync, _logger, SystemQuantityMissingSentinel);

    public IReadOnlyDictionary<string, int> QuantityCodeMappings =>
        new OnDemandResolvingDictionary<string, int>(_quantityCodeCache, GetOrCreateQuantityCodeIdAsync, _logger, SystemQuantityMissingSentinel);

    public async Task PreloadResourceTypesAsync(CancellationToken cancellationToken)
    {
        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            await LoadResourceTypesAsync(cancellationToken);
            _resourceTypesLoaded = true;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Ensures resource-type mappings are loaded, race-free under concurrent callers on a cold
    /// cache. Unlike a bare <c>ResourceTypeMappings.Count == 0</c> check (the bug this method
    /// fixes -- see docs/superpowers/specs/2026-07-20-sqlserver-search-param-cache-race-fix-design.md),
    /// completion is tracked by <see cref="_resourceTypesLoaded"/>, a dedicated flag set only after
    /// <see cref="LoadResourceTypesAsync"/> fully completes, while still holding <see cref="_dbLock"/>.
    /// Dictionary emptiness is not a valid completion signal here: <see cref="_resourceTypeCache"/> is
    /// live-visible to readers during its own population loop, so a caller whose fast-path check landed
    /// mid-load could otherwise observe a partially-populated dictionary. A concurrent caller arriving
    /// mid-load blocks on <see cref="_dbLock"/> until the in-flight load finishes and the flag flips,
    /// instead of reading a partially-populated dictionary. A no-op once the flag is set.
    /// </summary>
    public async Task EnsureResourceTypesPreloadedAsync(CancellationToken cancellationToken)
    {
        if (_resourceTypesLoaded)
        {
            return;
        }

        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            if (_resourceTypesLoaded)
            {
                return;
            }

            await LoadResourceTypesAsync(cancellationToken);
            _resourceTypesLoaded = true;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Query-and-populate body shared by <see cref="PreloadResourceTypesAsync"/> and
    /// <see cref="EnsureResourceTypesPreloadedAsync"/>. Does NOT acquire <see cref="_dbLock"/> --
    /// both callers already hold it. Never call this directly from outside those two methods.
    /// </summary>
    private async Task LoadResourceTypesAsync(CancellationToken cancellationToken)
    {
        using var command = new SqlCommand("SELECT ResourceTypeId, Name FROM dbo.ResourceType");
        var rows = await _sqlExecutionService.ExecuteReaderAsync(
            tenantId,
            command,
            reader => (Id: reader.GetInt16(0), Name: reader.GetString(1)),
            cancellationToken);

        foreach (var row in rows)
        {
            _resourceTypeCache[row.Name] = row.Id;
        }

        _logger.LogInformation("Preloaded {Count} resource types into cache", rows.Count);
    }

    public async Task PreloadSearchParamsAsync(int? maxRows, CancellationToken cancellationToken)
    {
        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            await LoadSearchParamsAsync(maxRows, cancellationToken);
            _searchParametersLoaded = true;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Ensures search-parameter mappings are loaded, race-free under concurrent callers on a cold
    /// cache -- see <see cref="EnsureResourceTypesPreloadedAsync"/>'s remarks; same shape, same
    /// flag-based completion signal, same bug fixed. Always loads the full catalog uncapped
    /// (<c>maxRows: null</c>) -- unlike the capped call <see cref="PreloadSearchParamsAsync"/> makes
    /// elsewhere, this mirrors the reference EF implementation's uncapped
    /// <c>SearchIndexReferenceDataCache.InitializeAsync</c>. A no-op once <see cref="_searchParametersLoaded"/>
    /// is set.
    /// </summary>
    public async Task EnsureSearchParametersPreloadedAsync(CancellationToken cancellationToken)
    {
        if (_searchParametersLoaded)
        {
            return;
        }

        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            if (_searchParametersLoaded)
            {
                return;
            }

            await LoadSearchParamsAsync(maxRows: null, cancellationToken);
            _searchParametersLoaded = true;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Query-and-populate body shared by <see cref="PreloadSearchParamsAsync"/> and
    /// <see cref="EnsureSearchParametersPreloadedAsync"/>. Does NOT acquire <see cref="_dbLock"/> --
    /// both callers already hold it. Never call this directly from outside those two methods.
    /// </summary>
    private async Task LoadSearchParamsAsync(int? maxRows, CancellationToken cancellationToken)
    {
        var commandText = maxRows.HasValue
            ? "SELECT TOP (@MaxRows) SearchParamId, Uri FROM dbo.SearchParam"
            : "SELECT SearchParamId, Uri FROM dbo.SearchParam";

        using var command = new SqlCommand(commandText);
        if (maxRows.HasValue)
        {
            command.Parameters.Add("@MaxRows", SqlDbType.Int).Value = maxRows.Value;
        }

        var rows = await _sqlExecutionService.ExecuteReaderAsync(
            tenantId,
            command,
            reader => (Id: reader.GetInt16(0), Uri: reader.GetString(1)),
            cancellationToken);

        foreach (var row in rows)
        {
            _searchParamCache[row.Uri] = row.Id;
            if (TestSearchParamRowInsertedHookAsync != null)
            {
                await TestSearchParamRowInsertedHookAsync();
            }
        }

        _logger.LogInformation(
            "Preloaded {Count} search parameters into cache{MaxRowsInfo}",
            rows.Count,
            maxRows.HasValue ? $" (limited to {maxRows.Value} rows)" : string.Empty);
    }

    public async Task<short?> GetResourceTypeIdAsync(string? resourceTypeName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(resourceTypeName))
        {
            return null;
        }

        if (_resourceTypeCache.TryGetValue(resourceTypeName, out var cachedId))
        {
            return cachedId == MissingSentinel ? null : cachedId;
        }

        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            if (_resourceTypeCache.TryGetValue(resourceTypeName, out cachedId))
            {
                return cachedId == MissingSentinel ? null : cachedId;
            }

            using var command = new SqlCommand("SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name = @Name");
            command.Parameters.Add("@Name", SqlDbType.NVarChar).Value = resourceTypeName;

            var rows = await _sqlExecutionService.ExecuteReaderAsync(
                tenantId, command, reader => reader.GetInt16(0), cancellationToken);

            if (rows.Count == 0)
            {
                // Deliberately NOT recorded, matching the reference EF implementation. dbo.ResourceType is
                // populated as types are first encountered, so "absent" is a transient state, not a stable
                // fact: remembering it poisons every later lookup and write of that type until something
                // overwrites the entry, and the row may be created by another process that no in-process
                // invalidation can observe. A repeated indexed singleton lookup on a rare miss is cheaper
                // than a wrong answer. Resource types are eagerly preloaded, so misses are genuinely rare.
                _logger.LogWarning("ResourceType not found: {ResourceTypeName}", resourceTypeName);
                return null;
            }

            var id = rows[0];
            _resourceTypeCache[resourceTypeName] = id;
            return id;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<short?> GetSearchParamIdAsync(string uri, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(uri))
        {
            return null;
        }

        if (_searchParamCache.TryGetValue(uri, out var cachedId))
        {
            return cachedId == MissingSentinel ? null : cachedId;
        }

        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            if (_searchParamCache.TryGetValue(uri, out cachedId))
            {
                return cachedId == MissingSentinel ? null : cachedId;
            }

            using var command = new SqlCommand("SELECT SearchParamId FROM dbo.SearchParam WHERE Uri = @Uri");
            // dbo.SearchParam.Uri is VARCHAR (not NVARCHAR) -- unlike System/QuantityCode.Value, so
            // this binds VarChar to avoid an implicit-conversion scan against the clustered PK.
            command.Parameters.Add("@Uri", SqlDbType.VarChar).Value = uri;

            var rows = await _sqlExecutionService.ExecuteReaderAsync(
                tenantId, command, reader => reader.GetInt16(0), cancellationToken);

            if (rows.Count == 0)
            {
                _logger.LogWarning("SearchParam not found for URI: {Uri}", uri);
                _searchParamCache[uri] = MissingSentinel;
                return null;
            }

            var id = rows[0];
            _searchParamCache[uri] = id;
            return id;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<int> GetOrCreateSystemIdAsync(string? systemUri, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(systemUri);

        if (_systemCache.TryGetValue(systemUri, out var cachedId))
        {
            return cachedId;
        }

        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            if (_systemCache.TryGetValue(systemUri, out cachedId))
            {
                return cachedId;
            }

            using var selectCommand = new SqlCommand("SELECT SystemId FROM dbo.System WHERE Value = @Value");
            selectCommand.Parameters.Add("@Value", SqlDbType.NVarChar).Value = systemUri;
            var existingRows = await _sqlExecutionService.ExecuteReaderAsync(
                tenantId, selectCommand, reader => reader.GetInt32(0), cancellationToken);

            if (existingRows.Count > 0)
            {
                var existingId = existingRows[0];
                _systemCache[systemUri] = existingId;
                _missingSystems.Forget(systemUri);
                return existingId;
            }

            // No unique-constraint catch/retry -- the original SearchIndexReferenceDataCache has
            // none either (relies on the in-process semaphore for single-process safety; a true
            // concurrent-insert race across processes is an existing, unaddressed gap this port
            // does not need to fix).
            using var insertCommand = new SqlCommand(
                "INSERT INTO dbo.System (Value) OUTPUT INSERTED.SystemId VALUES (@Value)");
            insertCommand.Parameters.Add("@Value", SqlDbType.NVarChar).Value = systemUri;
            var insertedRows = await _sqlExecutionService.ExecuteReaderAsync(
                tenantId, insertCommand, reader => reader.GetInt32(0), cancellationToken);

            var newId = insertedRows[0];
            _logger.LogDebug("Created new System entry: {SystemUri} -> {SystemId}", systemUri, newId);
            _systemCache[systemUri] = newId;

            // Forgetting the recorded miss is what stops a search that already probed this system from
            // continuing to report it missing now that it exists.
            _missingSystems.Forget(systemUri);
            return newId;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<int> GetOrCreateQuantityCodeIdAsync(string? code, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);

        if (_quantityCodeCache.TryGetValue(code, out var cachedId))
        {
            return cachedId;
        }

        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            if (_quantityCodeCache.TryGetValue(code, out cachedId))
            {
                return cachedId;
            }

            using var selectCommand = new SqlCommand("SELECT QuantityCodeId FROM dbo.QuantityCode WHERE Value = @Value");
            selectCommand.Parameters.Add("@Value", SqlDbType.NVarChar).Value = code;
            var existingRows = await _sqlExecutionService.ExecuteReaderAsync(
                tenantId, selectCommand, reader => reader.GetInt32(0), cancellationToken);

            if (existingRows.Count > 0)
            {
                var existingId = existingRows[0];
                _quantityCodeCache[code] = existingId;
                _missingQuantityCodes.Forget(code);
                return existingId;
            }

            using var insertCommand = new SqlCommand(
                "INSERT INTO dbo.QuantityCode (Value) OUTPUT INSERTED.QuantityCodeId VALUES (@Value)");
            insertCommand.Parameters.Add("@Value", SqlDbType.NVarChar).Value = code;
            var insertedRows = await _sqlExecutionService.ExecuteReaderAsync(
                tenantId, insertCommand, reader => reader.GetInt32(0), cancellationToken);

            var newId = insertedRows[0];
            _logger.LogDebug("Created new QuantityCode entry: {Code} -> {QuantityCodeId}", code, newId);
            _quantityCodeCache[code] = newId;
            _missingQuantityCodes.Forget(code);
            return newId;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Read-only, miss-returns-null system lookup for the search path. Unlike
    /// <see cref="GetOrCreateSystemIdAsync"/>, never inserts a new <c>dbo.System</c> row as a side
    /// effect -- an unresolved system just means "no match," not a silent database write.
    /// Caches only positive results in <see cref="_systemCache"/>: that cache is shared with the write
    /// path, which treats every cached integer as a real surrogate id, so a sentinel cannot live there
    /// without corrupting writes. Misses go to <see cref="_missingSystems"/>, consulted before the lock so
    /// a search naming unindexed terminology does not serialize behind ingest on every occurrence, and
    /// bounded by that cache's TTL so a row created out of process becomes visible on its own.
    /// </summary>
    public async Task<int?> TryGetSystemIdAsync(string systemUri, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(systemUri);

        if (_systemCache.TryGetValue(systemUri, out var cachedId))
        {
            return cachedId;
        }

        if (_missingSystems.IsKnownMissing(systemUri))
        {
            return null;
        }

        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            if (_systemCache.TryGetValue(systemUri, out cachedId))
            {
                return cachedId;
            }

            using var command = new SqlCommand("SELECT SystemId FROM dbo.System WHERE Value = @Value");
            command.Parameters.Add("@Value", SqlDbType.NVarChar).Value = systemUri;
            var rows = await _sqlExecutionService.ExecuteReaderAsync(
                tenantId, command, reader => reader.GetInt32(0), cancellationToken);

            if (rows.Count == 0)
            {
                _missingSystems.RecordMiss(systemUri);
                return null;
            }

            var id = rows[0];
            _systemCache[systemUri] = id;
            return id;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Read-only, miss-returns-null quantity-code lookup for the search path -- see
    /// <see cref="TryGetSystemIdAsync"/>'s remarks; same shape, same split between the shared positive
    /// cache and a separate bounded negative cache.
    /// </summary>
    public async Task<int?> TryGetQuantityCodeIdAsync(string code, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);

        if (_quantityCodeCache.TryGetValue(code, out var cachedId))
        {
            return cachedId;
        }

        if (_missingQuantityCodes.IsKnownMissing(code))
        {
            return null;
        }

        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            if (_quantityCodeCache.TryGetValue(code, out cachedId))
            {
                return cachedId;
            }

            using var command = new SqlCommand("SELECT QuantityCodeId FROM dbo.QuantityCode WHERE Value = @Value");
            command.Parameters.Add("@Value", SqlDbType.NVarChar).Value = code;
            var rows = await _sqlExecutionService.ExecuteReaderAsync(
                tenantId, command, reader => reader.GetInt32(0), cancellationToken);

            if (rows.Count == 0)
            {
                _missingQuantityCodes.RecordMiss(code);
                return null;
            }

            var id = rows[0];
            _quantityCodeCache[code] = id;
            return id;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Drops any recorded "this system is missing" answer for <paramref name="systemUri"/>, so a search
    /// that probed it beforehand stops reporting it missing.
    /// </summary>
    /// <remarks>
    /// The write path invalidates on its own (see <see cref="GetOrCreateSystemIdAsync"/>), so this exists
    /// for a writer that creates <c>dbo.System</c> rows without going through this cache. No such writer
    /// exists in this assembly today -- <c>SqlServerSystemRepository</c> delegates to the cache -- and this
    /// is a per-instance hook: it cannot reach a cache held for another tenant or another process, which
    /// remains the negative cache's TTL to bound.
    /// </remarks>
    public void ForgetMissingSystem(string? systemUri)
    {
        if (!string.IsNullOrEmpty(systemUri))
        {
            _missingSystems.Forget(systemUri);
        }
    }

    /// <summary>
    /// Drops any recorded "this quantity code is missing" answer for <paramref name="code"/> -- see
    /// <see cref="ForgetMissingSystem"/> for the reasoning and the same per-instance limitation.
    /// </summary>
    public void ForgetMissingQuantityCode(string? code)
    {
        if (!string.IsNullOrEmpty(code))
        {
            _missingQuantityCodes.Forget(code);
        }
    }

    public short? TryGetResourceTypeIdFromCache(string? resourceTypeName)
    {
        if (string.IsNullOrEmpty(resourceTypeName))
        {
            return null;
        }

        if (_resourceTypeCache.TryGetValue(resourceTypeName, out var cachedId))
        {
            return cachedId == MissingSentinel ? null : cachedId;
        }

        return null;
    }

    public short? TryGetSearchParamIdFromCache(string? uri)
    {
        if (string.IsNullOrEmpty(uri))
        {
            return null;
        }

        if (_searchParamCache.TryGetValue(uri, out var cachedId))
        {
            return cachedId == MissingSentinel ? null : cachedId;
        }

        return null;
    }

    // Task 6's GetOrCreateResourceTypeIdAsync helper calls this immediately after inserting a new
    // ResourceType row, to record the real ID directly -- bypassing GetResourceTypeIdAsync's
    // cache-miss sentinel entirely. Without this, the sentinel written by an earlier miss would
    // stay cached after the insert, causing every later lookup to report "not found" and re-insert.
    public void CacheResourceTypeId(string resourceTypeName, short resourceTypeId)
    {
        ArgumentException.ThrowIfNullOrEmpty(resourceTypeName);
        _resourceTypeCache[resourceTypeName] = resourceTypeId;
    }

    public void Dispose()
    {
        _dbLock.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Live, sentinel-filtering read-only view over a backing <see cref="ConcurrentDictionary{TKey,TValue}"/>
    /// of ResourceTypeId/SearchParamId caches. Wraps by reference (no snapshot copy); entries holding
    /// <see cref="MissingSentinel"/> are never surfaced.
    /// </summary>
    private sealed class SentinelFilteringDictionary(ConcurrentDictionary<string, short> inner) : IReadOnlyDictionary<string, short>
    {
        public short this[string key] => TryGetValue(key, out var value)
            ? value
            : throw new KeyNotFoundException($"The given key '{key}' was not present in the dictionary.");

        public IEnumerable<string> Keys => this.Select(kvp => kvp.Key);

        public IEnumerable<short> Values => this.Select(kvp => kvp.Value);

        public int Count => inner.Count(kvp => kvp.Value != MissingSentinel);

        public bool ContainsKey(string key) => inner.TryGetValue(key, out var value) && value != MissingSentinel;

        public bool TryGetValue(string key, out short value)
        {
            if (inner.TryGetValue(key, out var found) && found != MissingSentinel)
            {
                value = found;
                return true;
            }

            value = default;
            return false;
        }

        public IEnumerator<KeyValuePair<string, short>> GetEnumerator()
            => inner.Where(kvp => kvp.Value != MissingSentinel).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Read-only dictionary wrapper that resolves a cache miss on demand via <paramref name="resolveAsync"/>
    /// (synchronously, via <c>GetAwaiter().GetResult()</c> -- used from within the row generators'
    /// synchronous <c>TryGetValue</c> calls, where async/await isn't available). Ports EF's
    /// <c>LazyLoadingDictionary</c> pattern (Ignixa.DataLayer.SqlEntityFramework.Indexing.
    /// SearchIndexReferenceDataCache) for <see cref="SystemMappings"/>/<see cref="QuantityCodeMappings"/>
    /// specifically -- see docs/superpowers/specs/2026-07-21-sqlserver-system-quantitycode-selfheal-design.md
    /// for why this was missing and why it's safe (bounded blocking cost, no deadlock against
    /// <see cref="_dbLock"/>, since row generation always runs after Ensure*PreloadedAsync has already
    /// released it). Internal, not private, so tests can construct it directly with a fake resolver to
    /// exercise the failure path deterministically -- <see cref="GetOrCreateSystemIdAsync"/>/
    /// <see cref="GetOrCreateQuantityCodeIdAsync"/> essentially never throw under normal test conditions.
    /// Note the asymmetry by design: <see cref="TryGetValue"/> and the indexer resolve on demand, but
    /// <see cref="ContainsKey"/>/<see cref="Count"/>/enumeration reflect only what's already cached --
    /// required, not incidental: an existing test asserts <c>ContainsKey</c> on an unresolved key stays
    /// false without triggering a resolve (see SqlServerSearchIndexReferenceDataCacheTests.cs's
    /// GivenASystemIdInsertedThroughOneCacheInstance_... test).
    /// </summary>
    internal sealed class OnDemandResolvingDictionary<TKey, TValue>(
        ConcurrentDictionary<TKey, TValue> cache,
        Func<TKey, CancellationToken, Task<TValue>> resolveAsync,
        ILogger logger,
        TValue missingSentinel) : IReadOnlyDictionary<TKey, TValue>
        where TKey : notnull
    {
        public TValue this[TKey key] => TryGetValue(key, out var value)
            ? value
            : throw new KeyNotFoundException($"The given key '{key}' was not present in the dictionary.");

        public IEnumerable<TKey> Keys => cache.Keys;

        public IEnumerable<TValue> Values => cache.Values;

        public int Count => cache.Count;

        public bool ContainsKey(TKey key) => cache.ContainsKey(key);

        public bool TryGetValue(TKey key, out TValue value)
        {
            if (cache.TryGetValue(key, out value!) && !EqualityComparer<TValue>.Default.Equals(value, missingSentinel))
            {
                return true;
            }

            try
            {
                value = resolveAsync(key, CancellationToken.None).GetAwaiter().GetResult();
                cache[key] = value;
                return true;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to resolve {Key} on demand -- row skipped", key);
                value = default!;
                return false;
            }
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => cache.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
