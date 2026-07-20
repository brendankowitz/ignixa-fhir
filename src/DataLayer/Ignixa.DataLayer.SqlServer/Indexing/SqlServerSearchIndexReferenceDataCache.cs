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
/// </summary>
public sealed class SqlServerSearchIndexReferenceDataCache(
    ISqlExecutionService sqlExecutionService,
    int tenantId,
    ILogger<SqlServerSearchIndexReferenceDataCache> logger) : IDisposable
{
    private const short MissingSentinel = -1;

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

    public IReadOnlyDictionary<string, short> ResourceTypeMappings => new SentinelFilteringDictionary(_resourceTypeCache);

    public IReadOnlyDictionary<string, short> SearchParameterMappings => new SentinelFilteringDictionary(_searchParamCache);

    // No sentinel concept for System/QuantityCode (only ever populated with real, on-demand-created
    // IDs), so the backing ConcurrentDictionary can be returned directly -- a genuinely live view,
    // not a copy, matching SqlServerMergeRepository's expectation that it stays live.
    public IReadOnlyDictionary<string, int> SystemMappings => _systemCache;

    public IReadOnlyDictionary<string, int> QuantityCodeMappings => _quantityCodeCache;

    public async Task PreloadResourceTypesAsync(CancellationToken cancellationToken)
    {
        await _dbLock.WaitAsync(cancellationToken);
        try
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
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task PreloadSearchParamsAsync(int? maxRows, CancellationToken cancellationToken)
    {
        await _dbLock.WaitAsync(cancellationToken);
        try
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
            }

            _logger.LogInformation(
                "Preloaded {Count} search parameters into cache{MaxRowsInfo}",
                rows.Count,
                maxRows.HasValue ? $" (limited to {maxRows.Value} rows)" : string.Empty);
        }
        finally
        {
            _dbLock.Release();
        }
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
                _logger.LogWarning("ResourceType not found: {ResourceTypeName}", resourceTypeName);
                _resourceTypeCache[resourceTypeName] = MissingSentinel;
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
            return newId;
        }
        finally
        {
            _dbLock.Release();
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
}
