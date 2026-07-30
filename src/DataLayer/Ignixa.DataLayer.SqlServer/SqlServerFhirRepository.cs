using System.Data;
using System.Runtime.CompilerServices;
using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Models;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Raw-ADO.NET port of <c>SqlEntityFrameworkRepository</c> (Ignixa.DataLayer.SqlEntityFramework)
/// against the same legacy fhir-server schema, using <see cref="ISqlExecutionService"/> instead of
/// EF Core. Delegates bulk/index writes to <see cref="SqlServerMergeRepository"/>, history queries
/// to <see cref="SqlServerHistoryQueryExecutor"/>. Full port history and task-by-task rationale:
/// <c>docs/superpowers/plans/2026-07-20-ignixa-datalayer-sqlserver-phase-d.md</c>.
/// </summary>
public class SqlServerFhirRepository(
    ISqlExecutionService sqlExecutionService,
    int tenantId,
    GzipResourceCompressor compressor,
    SqlServerSearchIndexReferenceDataCache cache,
    SqlServerMergeRepository mergeRepository,
    ILogger<SqlServerFhirRepository> logger) : IFhirRepository
{
    private readonly ISqlExecutionService _sqlExecutionService =
        sqlExecutionService ?? throw new ArgumentNullException(nameof(sqlExecutionService));
    private readonly GzipResourceCompressor _compressor =
        compressor ?? throw new ArgumentNullException(nameof(compressor));
    private readonly SqlServerSearchIndexReferenceDataCache _cache =
        cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly SqlServerMergeRepository _mergeRepository =
        mergeRepository ?? throw new ArgumentNullException(nameof(mergeRepository));
    private readonly ILogger<SqlServerFhirRepository> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly int _tenantId = tenantId;
    private readonly SqlServerHistoryQueryExecutor _historyExecutor = new(sqlExecutionService, tenantId, compressor, logger);

    private static readonly string[] SearchIndexTables =
    [
        "ReferenceSearchParam",
        "TokenSearchParam",
        "TokenText",
        "StringSearchParam",
        "UriSearchParam",
        "NumberSearchParam",
        "QuantitySearchParam",
        "DateTimeSearchParam",
        "ReferenceTokenCompositeSearchParam",
        "TokenTokenCompositeSearchParam",
        "TokenDateTimeCompositeSearchParam",
        "TokenQuantityCompositeSearchParam",
        "TokenStringCompositeSearchParam",
        "TokenNumberNumberCompositeSearchParam",
        "ResourceWriteClaim"
    ];

    /// <inheritdoc/>
    public async ValueTask<SearchEntryResult?> GetAsync(ResourceKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        _logger.LogDebug("Getting resource {ResourceType}/{ResourceId}", key.ResourceType, key.Id);

        var resourceTypeId = await GetOrCreateResourceTypeIdAsync(key.ResourceType, cancellationToken);

        SqlCommand command;
        if (key.VersionId != null && int.TryParse(key.VersionId, out var version))
        {
            command = new SqlCommand(
                """
                SELECT r.ResourceId, r.Version, r.RawResource, r.IsDeleted, r.RequestMethod, t.CreateDate
                FROM dbo.Resource r LEFT JOIN dbo.Transactions t ON r.TransactionId = t.SurrogateIdRangeFirstValue
                WHERE r.ResourceTypeId = @ResourceTypeId AND r.ResourceId = @ResourceId AND r.Version = @Version;
                """);
            command.Parameters.Add("@Version", SqlDbType.Int).Value = version;
        }
        else
        {
            command = new SqlCommand(
                """
                SELECT TOP (1) r.ResourceId, r.Version, r.RawResource, r.IsDeleted, r.RequestMethod, t.CreateDate
                FROM dbo.Resource r LEFT JOIN dbo.Transactions t ON r.TransactionId = t.SurrogateIdRangeFirstValue
                WHERE r.ResourceTypeId = @ResourceTypeId AND r.ResourceId = @ResourceId AND r.IsHistory = 0
                ORDER BY r.Version DESC;
                """);
        }

        using (command)
        {
            command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
            command.Parameters.Add("@ResourceId", SqlDbType.VarChar).Value = key.Id;

            var rows = await _sqlExecutionService.ExecuteReaderAsync(_tenantId, command, ReadResourceRow, cancellationToken);

            if (rows.Count == 0)
            {
                _logger.LogDebug("Resource not found: {ResourceType}/{ResourceId}", key.ResourceType, key.Id);
                return null;
            }

            var row = rows[0];

            // NOTE: Do NOT filter out deleted resources here - return them with IsDeleted=true.
            // The API layer checks IsDeleted and returns 410 Gone (404 = never existed, 410 = deleted).
            var result = new SearchEntryResult(
                ResourceType: key.ResourceType,
                ResourceId: row.ResourceId,
                VersionId: row.Version.ToString(),
                LastModified: row.CreateDate ?? DateTimeOffset.UtcNow,
                ResourceBytes: _compressor.DecompressBytes(row.RawResource))
            {
                IsDeleted = row.IsDeleted,
                TenantId = key.TenantId,
            };

            _logger.LogDebug("Retrieved resource {ResourceType}/{ResourceId} version {Version}", key.ResourceType, key.Id, row.Version);

            return result;
        }
    }

    /// <inheritdoc/>
    public async ValueTask<UpdateResult> CreateOrUpdateAsync(ResourceWrapper resource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrEmpty(resource.ResourceType);
        ArgumentException.ThrowIfNullOrEmpty(resource.ResourceId);

        if (resource.Resource == null)
        {
            throw new ArgumentException("Resource is required", nameof(resource));
        }

        _logger.LogDebug("Creating/updating resource {ResourceType}/{ResourceId}", resource.ResourceType, resource.ResourceId);

        var transactionId = await GetNextTransactionIdAsync(cancellationToken);

        var resourceTypeId = await GetOrCreateResourceTypeIdAsync(resource.ResourceType, cancellationToken);

        var currentVersion = await GetCurrentVersionOrderedBySurrogateIdAsync(resourceTypeId, resource.ResourceId, cancellationToken);
        var newVersion = currentVersion.HasValue ? currentVersion.Value + 1 : 1;

        // Must happen BEFORE handing resource.Resource to the merge repository -- the merge path
        // compresses resource.Resource into RawResource bytes, so the version/timestamp needs to be
        // baked in first (matches legacy SqlEntityFrameworkRepository.cs:159-160 exactly).
        resource.Resource.Meta.VersionId = newVersion.ToString();
        resource.Resource.Meta.LastUpdatedOffset = transactionId.Value.ToDate();

        var resourceList = new[] { resource };
        var entryIndices = new[] { 0 };

        await _mergeRepository.MergeResourcesAsync(
            transactionId.Value,
            singleTransaction: true,
            resourceList,
            entryIndices,
            cancellationToken);

        await _mergeRepository.CommitTransactionAsync(
            transactionId: transactionId.Value,
            failureReason: null,
            cancellationToken: cancellationToken);

        await UpsertResourceTtlAsync(resourceTypeId, resource.ResourceId, resource.ExpiresAt, transactionId.Value, cancellationToken);

        _logger.LogInformation(
            "Created/updated resource {ResourceType}/{ResourceId} version {Version} via merge",
            resource.ResourceType, resource.ResourceId, newVersion);

        var compressedData = _compressor.SerializeAndCompress(resource.Resource);
        var lastModified = resource.Resource.Meta.LastUpdatedOffset ?? DateTimeOffset.UtcNow;

        var key = new ResourceKey(
            ResourceType: resource.ResourceType,
            Id: resource.ResourceId,
            VersionId: newVersion.ToString(),
            TenantId: resource.TenantId);

        return new UpdateResult(
            Key: key,
            ResourceBytes: _compressor.DecompressBytes(compressedData),
            LastModified: lastModified)
        {
            Request = resource.Request
        };
    }

    /// <inheritdoc/>
    public async ValueTask<ResourceKey?> DeleteAsync(
        ResourceKey key,
        ResourceRequest request,
        TransactionId? transactionId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogDebug("Deleting resource {ResourceType}/{ResourceId}", key.ResourceType, key.Id);

        var resourceTypeId = await GetOrCreateResourceTypeIdAsync(key.ResourceType, cancellationToken);

        var currentEntity = await GetCurrentResourceForDeleteAsync(resourceTypeId, key.Id, cancellationToken);

        if (currentEntity == null)
        {
            _logger.LogWarning(
                "Cannot delete {ResourceType}/{ResourceId}: resource never existed", key.ResourceType, key.Id);
            return null;
        }

        if (currentEntity.Value.IsDeleted)
        {
            _logger.LogDebug(
                "Resource {ResourceType}/{ResourceId} already deleted at version {Version} (idempotent)",
                key.ResourceType, key.Id, currentEntity.Value.Version);

            return new ResourceKey(key.ResourceType, key.Id, currentEntity.Value.Version.ToString(), key.TenantId);
        }

        var newVersion = currentEntity.Value.Version + 1;

        // Deliberate divergence from the legacy EF port: this method never allocates a transactionId
        // (no transaction-scoped delete), matching the documented semantics pinned directly by
        // SqlServerFhirRepositoryCrudTests -- see that file for the exact behavioral contract this
        // comment used to restate in full. This divergence is currently inert in production: the only
        // real caller (DeleteResourceHandler) always passes transactionId: null.
        using (var historyCommand = new SqlCommand(
            "UPDATE dbo.Resource SET IsHistory=1, HistoryTransactionId=@HistoryTransactionId WHERE ResourceSurrogateId=@ResourceSurrogateId"))
        {
            historyCommand.Parameters.Add("@HistoryTransactionId", SqlDbType.BigInt).Value =
                (object?)transactionId?.Value ?? DBNull.Value;
            historyCommand.Parameters.Add("@ResourceSurrogateId", SqlDbType.BigInt).Value = currentEntity.Value.ResourceSurrogateId;
            await _sqlExecutionService.ExecuteNonQueryAsync(_tenantId, historyCommand, cancellationToken);
        }

        var tombstoneJsonNode = new ResourceJsonNode
        {
            ResourceType = key.ResourceType,
            Id = key.Id,
            Meta = new Meta
            {
                VersionId = newVersion.ToString(),
                LastUpdatedOffset = DateTimeOffset.UtcNow
            }
        };
        var compressedTombstone = _compressor.SerializeAndCompress(tombstoneJsonNode);

        var newSurrogateId = await GetNextSurrogateIdAsync(cancellationToken);

        using (var insertCommand = new SqlCommand(
            """
            INSERT INTO dbo.Resource
                (ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash, TransactionId)
            VALUES
                (@ResourceTypeId, @ResourceId, @NewVersion, 0, @NewSurrogateId, 1, 'DELETE', @TombstoneBytes, 1, NULL, @TransactionId);
            """))
        {
            insertCommand.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
            insertCommand.Parameters.Add("@ResourceId", SqlDbType.VarChar).Value = key.Id;
            insertCommand.Parameters.Add("@NewVersion", SqlDbType.Int).Value = newVersion;
            insertCommand.Parameters.Add("@NewSurrogateId", SqlDbType.BigInt).Value = newSurrogateId;
            insertCommand.Parameters.Add("@TombstoneBytes", SqlDbType.VarBinary).Value = compressedTombstone;
            insertCommand.Parameters.Add("@TransactionId", SqlDbType.BigInt).Value =
                (object?)transactionId?.Value ?? DBNull.Value;
            await _sqlExecutionService.ExecuteNonQueryAsync(_tenantId, insertCommand, cancellationToken);
        }

        await UpsertResourceTtlAsync(resourceTypeId, key.Id, expiresAt: null, transactionId?.Value, cancellationToken);

        await DeleteSearchIndexEntriesAsync(currentEntity.Value.ResourceSurrogateId, cancellationToken);

        _logger.LogInformation(
            "Created tombstone for {ResourceType}/{ResourceId} version {Version}", key.ResourceType, key.Id, newVersion);

        return new ResourceKey(key.ResourceType, key.Id, newVersion.ToString(), key.TenantId);
    }

    /// <inheritdoc/>
    public async ValueTask<TransactionId> GetNextTransactionIdAsync(CancellationToken cancellationToken = default)
    {
        var (id, _) = await _mergeRepository.BeginTransactionAsync(1000, cancellationToken);
        return new TransactionId(id);
    }

    /// <inheritdoc/>
    public async ValueTask CommitTransactionAsync(TransactionId transactionId, CancellationToken cancellationToken = default)
    {
        await _mergeRepository.CommitTransactionAsync(transactionId.Value, failureReason: null, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ResourceKey>> BatchWriteAsync(
        TransactionId transactionId,
        IReadOnlyList<(string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes, string httpMethod, int entryIndex)> operations,
        CancellationToken cancellationToken = default)
    {
        // Note: transactionId is a struct, ArgumentNullException.ThrowIfNull doesn't make sense.
        ArgumentNullException.ThrowIfNull(operations);

        _logger.LogDebug(
            "Batch writing {Count} resources for transaction {TransactionId}",
            operations.Count,
            transactionId.Value);

        var resourceTypeMap = await ResolveResourceTypeIdsAsync(operations, cancellationToken);

        var resourceLookupKeys = operations
            .Select(op => (TypeId: resourceTypeMap[op.resourceType], op.resourceId))
            .Distinct()
            .ToList();

        var currentVersions = await GetExistingResourceVersionsAsync(resourceLookupKeys, cancellationToken);

        _logger.LogDebug(
            "Batch query found {ExistingCount} existing resources, {NewCount} are new",
            currentVersions.Count,
            operations.Count - currentVersions.Count);

        var resourceWrappers = await BuildResourceWrappersAsync(operations, resourceTypeMap, currentVersions, transactionId, cancellationToken);

        var entryIndices = operations.Select(op => op.entryIndex).ToList();

        // Delegate the actual write to the merge repository -- same mechanism as CreateOrUpdateAsync.
        // Does NOT commit internally: commit happens later via a separate CommitTransactionAsync call.
        await _mergeRepository.MergeResourcesAsync(
            transactionId.Value,
            singleTransaction: true,
            resourceWrappers,
            entryIndices,
            cancellationToken);

        var results = new List<ResourceKey>(operations.Count);
        for (var i = 0; i < operations.Count; i++)
        {
            results.Add(new ResourceKey(operations[i].resourceType, operations[i].resourceId, resourceWrappers[i].VersionId, null));
        }

        _logger.LogInformation(
            "Batch wrote {Count} resources for transaction {TransactionId}",
            operations.Count,
            transactionId.Value);

        return results;
    }

    // Validates surrogate IDs and versions BEFORE building the wrapper / sending to the database.
    // This replicates the stored procedure's own validation check to catch issues early with better
    // error messages.
    private Task<IReadOnlyList<ResourceWrapper>> BuildResourceWrappersAsync(
        IReadOnlyList<(string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes, string httpMethod, int entryIndex)> operations,
        Dictionary<string, short> resourceTypeMap,
        Dictionary<(short TypeId, string ResourceId), (int MaxVersion, long MaxSurrogateId)> currentVersions,
        TransactionId transactionId,
        CancellationToken cancellationToken)
    {
        var resourceWrappers = new List<ResourceWrapper>(operations.Count);

        foreach (var (resourceType, resourceId, resource, searchIndexes, httpMethod, entryIndex) in operations)
        {
            var resourceTypeId = resourceTypeMap[resourceType];
            var key = (resourceTypeId, resourceId);
            var newSurrogateId = transactionId.Value + entryIndex;

            var hasCurrentVersion = currentVersions.TryGetValue(key, out var existing);
            var newVersion = (hasCurrentVersion ? existing.MaxVersion : 0) + 1;

            if (hasCurrentVersion)
            {
                if (newVersion <= existing.MaxVersion)
                {
                    throw new InvalidOperationException(
                        $"Version constraint violation for {resourceType}/{resourceId}: " +
                        $"NewVersion={newVersion} <= PreviousVersion={existing.MaxVersion}");
                }

                if (newSurrogateId <= existing.MaxSurrogateId)
                {
                    throw new ResourceVersionConflictException(
                        resourceType,
                        resourceId,
                        newSurrogateId,
                        existing.MaxSurrogateId);
                }
            }

            // Update the JsonNode meta to reflect the calculated version -- ensures the stored JSON
            // has the correct meta.versionId and meta.lastUpdated (same IdHelper mechanism as
            // CreateOrUpdateAsync).
            resource.Meta.VersionId = newVersion.ToString();
            resource.Meta.LastUpdatedOffset = transactionId.Value.ToDate();

            var wrapper = new ResourceWrapper(
                ResourceType: resourceType,
                ResourceId: resourceId,
                VersionId: newVersion.ToString(),
                LastModified: resource.Meta.LastUpdatedOffset.Value,
                Resource: resource,
                Request: new ResourceRequest(httpMethod, $"{resourceType}/{resourceId}"),
                IsDeleted: false)
            {
                SearchIndices = searchIndexes,
                TenantId = null
            };

            resourceWrappers.Add(wrapper);
        }

        return Task.FromResult<IReadOnlyList<ResourceWrapper>>(resourceWrappers);
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<TransactionId>> GetStalledTransactionsAsync(
        TimeSpan stallThreshold,
        CancellationToken cancellationToken = default)
    {
        var threshold = DateTime.UtcNow - stallThreshold;

        _logger.LogDebug(
            "Querying for stalled transactions (IsCompleted = false, HeartbeatDate < {Threshold})",
            threshold);

        using var command = new SqlCommand(
            "SELECT SurrogateIdRangeFirstValue FROM dbo.Transactions WHERE IsCompleted = 0 AND HeartbeatDate < @StalledBefore;");
        command.Parameters.Add("@StalledBefore", SqlDbType.DateTime).Value = threshold;

        var stalledTransactions = await _sqlExecutionService.ExecuteReaderAsync(
            _tenantId, command, reader => new TransactionId(reader.GetInt64(0)), cancellationToken);

        if (stalledTransactions.Count > 0)
        {
            _logger.LogWarning(
                "Found {Count} stalled transactions in database (threshold: {Threshold})",
                stalledTransactions.Count,
                threshold);
        }
        else
        {
            _logger.LogDebug("No stalled transactions found");
        }

        return stalledTransactions;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<SearchEntryResult> GetResourceHistoryAsync(
        ResourceKey key,
        HistoryQueryParameters parameters,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(parameters);

        parameters = parameters.Validate();

        _logger.LogDebug(
            "Getting history for resource {ResourceType}/{ResourceId} (count={Count}, offset={Offset})",
            key.ResourceType, key.Id, parameters.Count, parameters.Offset);

        var resourceTypeId = await GetOrCreateResourceTypeIdAsync(key.ResourceType, cancellationToken);

        await foreach (var result in _historyExecutor.GetResourceHistoryAsync(
            resourceTypeId, key.ResourceType, key.Id, parameters, cancellationToken))
        {
            yield return result;
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<SearchEntryResult> GetTypeHistoryAsync(
        string resourceType,
        int tenantId,
        HistoryQueryParameters parameters,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(resourceType);
        ArgumentNullException.ThrowIfNull(parameters);

        parameters = parameters.Validate();

        _logger.LogDebug(
            "Getting history for resource type {ResourceType} (count={Count}, offset={Offset})",
            resourceType, parameters.Count, parameters.Offset);

        var resourceTypeId = await GetOrCreateResourceTypeIdAsync(resourceType, cancellationToken);

        await foreach (var result in _historyExecutor.GetTypeHistoryAsync(
            resourceTypeId, resourceType, parameters, cancellationToken))
        {
            yield return result;
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<SearchEntryResult> GetSystemHistoryAsync(
        int tenantId,
        HistoryQueryParameters parameters,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        parameters = parameters.Validate();

        _logger.LogDebug(
            "Getting system-wide history (count={Count}, offset={Offset})",
            parameters.Count, parameters.Offset);

        await foreach (var result in _historyExecutor.GetSystemHistoryAsync(parameters, cancellationToken))
        {
            yield return result;
        }
    }

    /// <inheritdoc/>
    // Ports SqlEntityFrameworkRepository.cs:934-967's 3-way LINQ join (ResourceTtl JOIN Resource JOIN
    // ResourceType) to raw SQL. The Resource join restricts to current (non-history, non-deleted) rows
    // -- a resource can carry a stale ResourceTtl row after being superseded or deleted, and TTL
    // cleanup must never try to hard-delete something that isn't the live current version.
    public async Task<IReadOnlyList<ExpiredResourceInfo>> GetExpiredResourcesAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        _logger.LogDebug(
            "Querying for expired resources (ExpiresAt < {Now}, limit {BatchSize})",
            now,
            batchSize);

        using var command = new SqlCommand(
            """
            SELECT TOP (@BatchSize) t.ResourceTypeId, t.ResourceId, t.ExpiresAt, rt.Name
            FROM dbo.ResourceTtl t
            JOIN dbo.Resource r ON r.ResourceTypeId = t.ResourceTypeId AND r.ResourceId = t.ResourceId AND r.IsHistory = 0 AND r.IsDeleted = 0
            JOIN dbo.ResourceType rt ON rt.ResourceTypeId = t.ResourceTypeId
            WHERE t.ExpiresAt < @Now;
            """);
        command.Parameters.Add("@BatchSize", SqlDbType.Int).Value = batchSize;
        command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;

        var expiredResources = await _sqlExecutionService.ExecuteReaderAsync(
            _tenantId,
            command,
            reader => new ExpiredResourceInfo(
                reader.GetInt16(0),
                reader.GetString(1),
                reader.GetDateTimeOffset(2),
                reader.GetString(3)),
            cancellationToken);

        _logger.LogDebug(
            "Found {Count} expired resources",
            expiredResources.Count);

        return expiredResources;
    }

    /// <inheritdoc/>
    // Ports SqlEntityFrameworkRepository.cs:989-1026 nearly verbatim -- already raw SQL in the
    // original (the one method in the whole port with no LINQ to translate), so only the execution
    // wrapper changes (ExecuteSqlInterpolatedAsync -> a parameterized SqlCommand via
    // ISqlExecutionService). Statement sequence unchanged: stash surrogate IDs, delete every
    // search-index table row for them, delete every dbo.Resource row (current + history), then delete
    // the dbo.ResourceTtl entry.
    public async Task HardDeleteResourceAsync(
        short resourceTypeId,
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Hard deleting resource: ResourceTypeId={ResourceTypeId}, ResourceId={ResourceId}",
            resourceTypeId,
            resourceId);

        var deleteStatements = string.Join("\n              ", SearchIndexTables.Select(table =>
            $"DELETE FROM dbo.{table} WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds);"));

        // CA2100 suppressed: deleteStatements is built exclusively from the fixed, hardcoded
        // SearchIndexTables array above -- never from caller/user input -- matching the identical
        // rationale used by DeleteSearchIndexEntriesAsync above.
#pragma warning disable CA2100
        using var command = new SqlCommand(
            $"""
            -- Create temp table to hold surrogate IDs
            DECLARE @SurrogateIds TABLE (ResourceSurrogateId BIGINT PRIMARY KEY);

            -- Find all surrogate IDs for this resource
            INSERT INTO @SurrogateIds (ResourceSurrogateId)
            SELECT ResourceSurrogateId
            FROM dbo.Resource
            WHERE ResourceTypeId = @ResourceTypeId AND ResourceId = @ResourceId;

            -- Delete all search parameter indexes
            {deleteStatements}

            -- Delete all resource versions (current + history)
            DELETE FROM dbo.Resource WHERE ResourceTypeId = @ResourceTypeId AND ResourceId = @ResourceId;

            -- Delete TTL entry (after successfully deleting resource)
            DELETE FROM dbo.ResourceTtl WHERE ResourceTypeId = @ResourceTypeId AND ResourceId = @ResourceId;
            """);
#pragma warning restore CA2100
        command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
        command.Parameters.Add("@ResourceId", SqlDbType.VarChar).Value = resourceId;

        await _sqlExecutionService.ExecuteNonQueryAsync(_tenantId, command, cancellationToken);

        _logger.LogInformation(
            "Successfully hard deleted resource: ResourceTypeId={ResourceTypeId}, ResourceId={ResourceId}",
            resourceTypeId,
            resourceId);
    }

    // Corrected during plan review: an earlier draft never updated the cache after inserting, so every
    // subsequent call for the same new type name would conclude "still missing" and attempt to insert
    // AGAIN (a duplicate row / unique-constraint violation on the second caller). CacheResourceTypeId
    // records the freshly-inserted ID directly, which is what makes the second call a cache hit.
    // Note the cache deliberately does NOT remember a resource-type miss, so the GetResourceTypeIdAsync
    // call below re-queries each time it is reached -- see that method's miss branch for why.
    private async Task<short> GetOrCreateResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken)
    {
        var cached = _cache.TryGetResourceTypeIdFromCache(resourceType);
        if (cached.HasValue)
        {
            return cached.Value;
        }

        var id = await _cache.GetResourceTypeIdAsync(resourceType, cancellationToken);
        if (id.HasValue)
        {
            return id.Value;
        }

        using var command = new SqlCommand(
            "INSERT INTO dbo.ResourceType (Name) OUTPUT INSERTED.ResourceTypeId VALUES (@Name)");
        command.Parameters.AddWithValue("@Name", resourceType);
        var results = await _sqlExecutionService.ExecuteReaderAsync(_tenantId, command, reader => reader.GetInt16(0), cancellationToken);
        var newId = results[0];
        _cache.CacheResourceTypeId(resourceType, newId);
        return newId;
    }

    // BatchWriteAsync's resource-type resolution: cache-first (no DB round trip on a hit), then ONE
    // batched "WHERE Name IN (...)" query for every cache miss (not one query per miss), then
    // GetOrCreateResourceTypeIdAsync for anything still missing after the batch query (new FHIR
    // resource types -- very rare). Mirrors SqlEntityFrameworkRepository.cs:353-410.
    private async Task<Dictionary<string, short>> ResolveResourceTypeIdsAsync(
        IReadOnlyList<(string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes, string httpMethod, int entryIndex)> operations,
        CancellationToken cancellationToken)
    {
        var uniqueResourceTypes = operations.Select(op => op.resourceType).Distinct().ToList();

        var resourceTypeMap = new Dictionary<string, short>();
        var cacheMisses = new List<string>();

        foreach (var resourceType in uniqueResourceTypes)
        {
            var cachedId = _cache.TryGetResourceTypeIdFromCache(resourceType);
            if (cachedId.HasValue)
            {
                resourceTypeMap[resourceType] = cachedId.Value;
            }
            else
            {
                cacheMisses.Add(resourceType);
            }
        }

        if (cacheMisses.Count == 0)
        {
            return resourceTypeMap;
        }

        _logger.LogDebug(
            "BatchWrite: {CacheHits} cache hits, {CacheMisses} cache misses for resource types",
            uniqueResourceTypes.Count - cacheMisses.Count,
            cacheMisses.Count);

        var parameterNames = new string[cacheMisses.Count];
        for (var i = 0; i < cacheMisses.Count; i++)
        {
            parameterNames[i] = $"@Name{i}";
        }

        // CA2100 suppressed: the query text is built purely from a fixed sequence of numbered
        // placeholders (@Name0, @Name1, ...) whose count is bounded by the batch's own distinct
        // resource-type count -- actual values always flow through parameters, never string
        // concatenation. Same rationale as DeleteSearchIndexEntriesAsync below.
#pragma warning disable CA2100
        using var command = new SqlCommand(
            $"SELECT ResourceTypeId, Name FROM dbo.ResourceType WHERE Name IN ({string.Join(", ", parameterNames)})");
#pragma warning restore CA2100
        for (var i = 0; i < cacheMisses.Count; i++)
        {
            command.Parameters.Add(parameterNames[i], SqlDbType.NVarChar).Value = cacheMisses[i];
        }

        var dbResults = await _sqlExecutionService.ExecuteReaderAsync(
            _tenantId, command, reader => (Id: reader.GetInt16(0), Name: reader.GetString(1)), cancellationToken);

        foreach (var (id, name) in dbResults)
        {
            resourceTypeMap[name] = id;
            _cache.CacheResourceTypeId(name, id);
            cacheMisses.Remove(name);
        }

        if (cacheMisses.Count > 0)
        {
            _logger.LogWarning(
                "BatchWrite: Creating {NewTypeCount} new ResourceTypes (unusual): {Types}",
                cacheMisses.Count,
                string.Join(", ", cacheMisses));

            foreach (var resourceType in cacheMisses)
            {
                resourceTypeMap[resourceType] = await GetOrCreateResourceTypeIdAsync(resourceType, cancellationToken);
            }
        }

        return resourceTypeMap;
    }

    // BatchWriteAsync's existing-resource lookup, chunked at exactly 100 items per round trip.
    // SQL Server doesn't support tuple IN directly, so each chunk is joined against a VALUES
    // table-value constructor -- one round trip per 100-item chunk, not N round trips. The 100-item
    // chunk size mirrors SqlEntityFrameworkRepository.cs:423-446; that original comment's own
    // rationale (EF Core's expression-tree compiler stack-overflowing on large Contains() lists)
    // doesn't apply to hand-written SQL, but the chunking itself is kept: SQL Server's own
    // parameter-count limits and query-plan size make very large IN/parameterized-list clauses a
    // real, separate concern.
    private async Task<Dictionary<(short TypeId, string ResourceId), (int MaxVersion, long MaxSurrogateId)>> GetExistingResourceVersionsAsync(
        IReadOnlyList<(short TypeId, string resourceId)> resourceLookupKeys,
        CancellationToken cancellationToken)
    {
        var currentVersions = new Dictionary<(short, string), (int MaxVersion, long MaxSurrogateId)>();

        const int chunkSize = 100;
        foreach (var chunk in resourceLookupKeys.Chunk(chunkSize))
        {
            var typeParamNames = new string[chunk.Length];
            var idParamNames = new string[chunk.Length];
            var valuesParts = new string[chunk.Length];
            for (var i = 0; i < chunk.Length; i++)
            {
                typeParamNames[i] = $"@Type{i}";
                idParamNames[i] = $"@Id{i}";
                valuesParts[i] = $"({typeParamNames[i]}, {idParamNames[i]})";
            }

            // CA2100 suppressed: same rationale as ResolveResourceTypeIdsAsync above -- the query
            // text is built purely from a fixed sequence of numbered placeholders bounded by this
            // chunk's own size (<= 100), with actual values always flowing through parameters.
#pragma warning disable CA2100
            using var command = new SqlCommand(
                $"""
                SELECT r.ResourceTypeId, r.ResourceId, r.Version, r.ResourceSurrogateId
                FROM dbo.Resource r
                INNER JOIN (VALUES {string.Join(", ", valuesParts)}) AS k(TypeId, ResourceId)
                    ON r.ResourceTypeId = k.TypeId AND r.ResourceId = k.ResourceId
                WHERE r.IsHistory = 0;
                """);
#pragma warning restore CA2100
            for (var i = 0; i < chunk.Length; i++)
            {
                command.Parameters.Add(typeParamNames[i], SqlDbType.SmallInt).Value = chunk[i].TypeId;
                command.Parameters.Add(idParamNames[i], SqlDbType.VarChar).Value = chunk[i].resourceId;
            }

            var rows = await _sqlExecutionService.ExecuteReaderAsync(
                _tenantId,
                command,
                reader => (
                    TypeId: reader.GetInt16(0),
                    ResourceId: reader.GetString(1),
                    Version: reader.GetInt32(2),
                    SurrogateId: reader.GetInt64(3)),
                cancellationToken);

            foreach (var row in rows)
            {
                var key = (row.TypeId, row.ResourceId);
                if (currentVersions.TryGetValue(key, out var existing))
                {
                    currentVersions[key] = (Math.Max(existing.MaxVersion, row.Version), Math.Max(existing.MaxSurrogateId, row.SurrogateId));
                }
                else
                {
                    currentVersions[key] = (row.Version, row.SurrogateId);
                }
            }
        }

        return currentVersions;
    }

    private async Task<long> GetNextSurrogateIdAsync(CancellationToken cancellationToken)
    {
        using var command = new SqlCommand("SELECT NEXT VALUE FOR dbo.ResourceSurrogateIdUniquifierSequence");
        var results = await _sqlExecutionService.ExecuteReaderAsync(_tenantId, command, reader => reader.GetInt32(0), cancellationToken);
        var sequenceValue = results[0];
        return (long)(DateTimeOffset.UtcNow - DateTimeOffset.MinValue).TotalMilliseconds * 80000 + sequenceValue;
    }

    private async Task UpsertResourceTtlAsync(
        short resourceTypeId,
        string resourceId,
        DateTimeOffset? expiresAt,
        long? transactionId,
        CancellationToken cancellationToken)
    {
        if (expiresAt.HasValue)
        {
            using var command = new SqlCommand(
                """
                MERGE dbo.ResourceTtl AS target
                USING (SELECT @ResourceTypeId AS ResourceTypeId, @ResourceId AS ResourceId) AS source
                ON target.ResourceTypeId = source.ResourceTypeId AND target.ResourceId = source.ResourceId
                WHEN MATCHED THEN UPDATE SET ExpiresAt = @ExpiresAt, TransactionId = @TransactionId
                WHEN NOT MATCHED THEN INSERT (ResourceTypeId, ResourceId, ExpiresAt, TransactionId) VALUES (@ResourceTypeId, @ResourceId, @ExpiresAt, @TransactionId);
                """);
            command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
            command.Parameters.Add("@ResourceId", SqlDbType.VarChar).Value = resourceId;
            command.Parameters.Add("@ExpiresAt", SqlDbType.DateTimeOffset).Value = expiresAt.Value;
            command.Parameters.Add("@TransactionId", SqlDbType.BigInt).Value = (object?)transactionId ?? DBNull.Value;
            await _sqlExecutionService.ExecuteNonQueryAsync(_tenantId, command, cancellationToken);
        }
        else
        {
            using var command = new SqlCommand(
                "DELETE FROM dbo.ResourceTtl WHERE ResourceTypeId = @ResourceTypeId AND ResourceId = @ResourceId;");
            command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
            command.Parameters.Add("@ResourceId", SqlDbType.VarChar).Value = resourceId;
            await _sqlExecutionService.ExecuteNonQueryAsync(_tenantId, command, cancellationToken);
        }
    }

    private async Task DeleteSearchIndexEntriesAsync(long resourceSurrogateId, CancellationToken cancellationToken)
    {
        var deleteStatements = string.Join(
            "\n",
            SearchIndexTables.Select(table => $"DELETE FROM dbo.{table} WHERE ResourceSurrogateId = @ResourceSurrogateId;"));

        // CA2100 suppressed: deleteStatements is built exclusively from the fixed, hardcoded
        // SearchIndexTables array above -- never from caller/user input -- matching the identical
        // rationale used by SqlEntityFrameworkRepository.DeleteSearchIndexEntriesAsync.
#pragma warning disable CA2100
        using var command = new SqlCommand(deleteStatements);
#pragma warning restore CA2100
        command.Parameters.Add("@ResourceSurrogateId", SqlDbType.BigInt).Value = resourceSurrogateId;
        await _sqlExecutionService.ExecuteNonQueryAsync(_tenantId, command, cancellationToken);

        _logger.LogDebug("Deleted search index entries for ResourceSurrogateId={ResourceSurrogateId}", resourceSurrogateId);
    }

    private async Task<int?> GetCurrentVersionOrderedBySurrogateIdAsync(short resourceTypeId, string resourceId, CancellationToken cancellationToken)
    {
        using var command = new SqlCommand(
            "SELECT TOP (1) Version FROM dbo.Resource WHERE ResourceTypeId = @ResourceTypeId AND ResourceId = @ResourceId AND IsHistory = 0 ORDER BY ResourceSurrogateId DESC");
        command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
        command.Parameters.Add("@ResourceId", SqlDbType.VarChar).Value = resourceId;

        var rows = await _sqlExecutionService.ExecuteReaderAsync(_tenantId, command, reader => reader.GetInt32(0), cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    private async Task<(long ResourceSurrogateId, int Version, bool IsDeleted)?> GetCurrentResourceForDeleteAsync(
        short resourceTypeId, string resourceId, CancellationToken cancellationToken)
    {
        using var command = new SqlCommand(
            "SELECT TOP (1) ResourceSurrogateId, Version, IsDeleted FROM dbo.Resource WHERE ResourceTypeId = @ResourceTypeId AND ResourceId = @ResourceId AND IsHistory = 0 ORDER BY Version DESC");
        command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
        command.Parameters.Add("@ResourceId", SqlDbType.VarChar).Value = resourceId;

        var rows = await _sqlExecutionService.ExecuteReaderAsync(
            _tenantId,
            command,
            reader => (ResourceSurrogateId: reader.GetInt64(0), Version: reader.GetInt32(1), IsDeleted: reader.GetBoolean(2)),
            cancellationToken);

        return rows.Count == 0 ? null : rows[0];
    }

    private static (string ResourceId, int Version, byte[] RawResource, bool IsDeleted, string? RequestMethod, DateTimeOffset? CreateDate) ReadResourceRow(SqlDataReader reader)
    {
        return (
            reader.GetString(0),
            reader.GetInt32(1),
            (byte[])reader[2],
            reader.GetBoolean(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc)));
    }
}
