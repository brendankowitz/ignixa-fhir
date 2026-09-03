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
            // The INDEX hint is load-bearing, not a performance tweak. Left to itself the optimizer seeks
            // IX_Resource_ResourceTypeId_ResourceId_Version ORDERED BACKWARD and evaluates IsHistory = 0 as a
            // residual on the key lookup, because IsHistory is not in that index at all. A soft delete never
            // touches that index, so a reader racing one positions on the live version, blocks on the
            // clustered row the delete holds X-locked, and after the commit re-examines the row it had
            // already committed to -- now IsHistory = 1 -- and rejects it. The tombstone's higher version
            // sorts above the scan's start position and a backward scan never revisits it, so the read
            // returns nothing and the API answers 404 "never existed" for a resource that certainly did.
            // Measured on a 400,000-row table: 8 of 30 racing reads, and 100% of reads forced to start
            // mid-delete. Under READ_COMMITTED_SNAPSHOT this cannot happen, but RCSI is not set anywhere in
            // this product, so every deployed tenant runs locking READ COMMITTED and is exposed.
            //
            // IX_Resource_ResourceTypeId_ResourceId is filtered on IsHistory = 0, so the history flip
            // DELETES its entry and the tombstone INSERT adds one back under the identical
            // (ResourceTypeId, ResourceId) key. There is nothing for the reader to be positioned past: it
            // blocks at the seek and, once the delete commits, finds the tombstone. Measured 0 of 30, with
            // every racing read returning the tombstone (410 Gone) rather than merely not returning null.
            //
            // If that index is ever renamed or dropped this query fails outright with "index does not
            // exist" rather than silently regressing -- which is the safer failure, and is pinned by
            // GivenTheReadIsForcedToStartMidDelete_WhenTheDeleteCommits_ThenTheReadSeesTheTombstone and by
            // GivenTheCurrentResourceRead_WhenTheSchemaIsDeployed_ThenTheIndexItIsHintedOntoExistsAsAFilteredUniqueIndex.
            command = new SqlCommand(
                """
                SELECT TOP (1) r.ResourceId, r.Version, r.RawResource, r.IsDeleted, r.RequestMethod, t.CreateDate
                FROM dbo.Resource r WITH (INDEX(IX_Resource_ResourceTypeId_ResourceId))
                LEFT JOIN dbo.Transactions t ON r.TransactionId = t.SurrogateIdRangeFirstValue
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

        await UpsertResourceTtlAsync(
            transaction: null, resourceTypeId, resource.ResourceId, resource.ExpiresAt, transactionId.Value, cancellationToken);

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
        var currentSurrogateId = currentEntity.Value.ResourceSurrogateId;

        // Computed here, OUTSIDE the unit of work below, because that callback can be re-run from the top
        // after a transient fault and must produce the same delete each time. Both values are safe to reuse
        // across attempts precisely because the rollback undid the attempt that used them: the tombstone's
        // meta.lastUpdated stays the instant the delete was asked for rather than drifting to whenever the
        // last retry happened, and the surrogate ID stays one ID per delete instead of burning a fresh
        // sequence value (and a fresh round trip) per attempt.
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

        // All four effects -- history flip, tombstone insert, TTL removal, search-index wipe -- commit
        // together or not at all. What the EF port expressed as one SaveChangesAsync became four
        // independently auto-committed statements on four connections in the raw-ADO.NET port, and between
        // the first and the second the resource has NO current row at all -- a state that is COMMITTED and
        // therefore readable by anyone, under any isolation level. A crash in that same gap is permanent:
        // no current row to read, and the next PUT computes version 1 again and collides with the surviving
        // history row on IX_Resource_ResourceTypeId_ResourceId_Version.
        //
        // What this transaction does NOT fix on its own is the 404 a concurrent GET can still get. That
        // anomaly outlived the non-atomic shape: it comes from the read's plan, not from any committed
        // state, and it was still measured at 8 of 30 racing reads with this transaction already in place.
        // Closing it took the INDEX hint on GetAsync's current-resource read -- see the comment there.
        await _sqlExecutionService.ExecuteInTransactionAsync(
            _tenantId,
            async (transaction, ct) =>
            {
                // ResourceTypeId and IsHistory both matter here. ResourceSurrogateId is only unique WITHIN a
                // resource type (PKC_Resource is keyed on both), and the surrogate ID came from a read that
                // committed before this transaction began -- so without "IsHistory = 0" a writer that
                // versioned the row in between would leave this re-stamping an already-history row,
                // reporting success, and inserting the tombstone at a version that is no longer current.
                // Zero rows means exactly that happened.
                using (var historyCommand = new SqlCommand(
                    """
                    UPDATE dbo.Resource SET IsHistory = 1, HistoryTransactionId = @HistoryTransactionId
                    WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId = @ResourceSurrogateId AND IsHistory = 0;
                    """))
                {
                    historyCommand.Parameters.Add("@HistoryTransactionId", SqlDbType.BigInt).Value =
                        (object?)transactionId?.Value ?? DBNull.Value;
                    historyCommand.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
                    historyCommand.Parameters.Add("@ResourceSurrogateId", SqlDbType.BigInt).Value = currentSurrogateId;

                    var flippedRows = await transaction.ExecuteNonQueryAsync(historyCommand, ct);
                    if (flippedRows == 0)
                    {
                        throw new ResourceVersionConflictException(key.ResourceType, key.Id, newSurrogateId, currentSurrogateId);
                    }
                }

                // Deliberate divergence from the legacy EF port: this method never allocates a transactionId
                // (no transaction-scoped delete), matching the documented semantics pinned directly by
                // SqlServerFhirRepositoryCrudTests -- see that file for the exact behavioral contract this
                // comment used to restate in full. This divergence is currently inert in production: the only
                // real caller (DeleteResourceHandler) always passes transactionId: null.
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
                    await transaction.ExecuteNonQueryAsync(insertCommand, ct);
                }

                await UpsertResourceTtlAsync(transaction, resourceTypeId, key.Id, expiresAt: null, transactionId?.Value, ct);

                await DeleteSearchIndexEntriesAsync(transaction, currentSurrogateId, ct);
            },
            cancellationToken);

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

        // One multi-statement batch is not one transaction: with no BEGIN TRAN each statement
        // auto-commits on its own, so a failure part-way through leaves the search-index rows deleted
        // and their dbo.Resource rows still present -- rows nothing will ever revisit, because the next
        // hard delete finds no resource to collect surrogate IDs from. The EF original had the same
        // shape; it is not a regression, but it is the same missing primitive as the soft-delete path
        // above and the same one-line fix.
        await _sqlExecutionService.ExecuteInTransactionAsync(
            _tenantId,
            async (transaction, ct) =>
            {
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

                await transaction.ExecuteNonQueryAsync(command, ct);
            },
            cancellationToken);

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

        // NonIdempotent: an unguarded INSERT that comes through ExecuteReaderAsync only because it needs the
        // generated ResourceTypeId back. A -2 command timeout does not prove the server did not commit it,
        // and a retry would insert the name a second time. Name is dbo.ResourceType's primary key, so that
        // is loud rather than silent -- but a duplicate-key error on a type the caller just created is a
        // failure invented by the retry, and the timeout the server actually caused is the honest one.
        var results = await _sqlExecutionService.ExecuteReaderAsync(
            _tenantId, command, reader => reader.GetInt16(0), cancellationToken, SqlCommandIdempotency.NonIdempotent);
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

    // Left Idempotent even though NEXT VALUE FOR mutates: a retry does not apply anything twice, it just
    // consumes another value and hands back that one instead. The caller never saw the value the failed
    // attempt may have burned, and a sequence is allowed to have gaps -- NEXT VALUE FOR is not rolled back
    // by a transaction either. Declining the retry would fail an entire delete over a transient blip.
    private async Task<long> GetNextSurrogateIdAsync(CancellationToken cancellationToken)
    {
        using var command = new SqlCommand("SELECT NEXT VALUE FOR dbo.ResourceSurrogateIdUniquifierSequence");
        var results = await _sqlExecutionService.ExecuteReaderAsync(_tenantId, command, reader => reader.GetInt32(0), cancellationToken);
        var sequenceValue = results[0];
        return (long)(DateTimeOffset.UtcNow - DateTimeOffset.MinValue).TotalMilliseconds * 80000 + sequenceValue;
    }

    /// <param name="transaction">
    /// The unit of work to enlist in, or <c>null</c> to run standalone on its own connection.
    /// <see cref="DeleteAsync"/> passes one because the TTL removal has to commit with the tombstone;
    /// <see cref="CreateOrUpdateAsync"/> passes null because there its write is the only statement.
    /// </param>
    private async Task UpsertResourceTtlAsync(
        ISqlTransactionContext? transaction,
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
            await ExecuteNonQueryAsync(transaction, command, cancellationToken);
        }
        else
        {
            using var command = new SqlCommand(
                "DELETE FROM dbo.ResourceTtl WHERE ResourceTypeId = @ResourceTypeId AND ResourceId = @ResourceId;");
            command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
            command.Parameters.Add("@ResourceId", SqlDbType.VarChar).Value = resourceId;
            await ExecuteNonQueryAsync(transaction, command, cancellationToken);
        }
    }

    private Task<int> ExecuteNonQueryAsync(
        ISqlTransactionContext? transaction, SqlCommand command, CancellationToken cancellationToken)
        => transaction is null
            ? _sqlExecutionService.ExecuteNonQueryAsync(_tenantId, command, cancellationToken)
            : transaction.ExecuteNonQueryAsync(command, cancellationToken);

    /// <summary>
    /// Wipes every search-index row for one resource version. Takes the unit of work rather than running
    /// standalone because its only caller, <see cref="DeleteAsync"/>, has to have this land with the
    /// tombstone: indexes swept without a tombstone make the resource unfindable while it is still current.
    /// </summary>
    private async Task DeleteSearchIndexEntriesAsync(
        ISqlTransactionContext transaction, long resourceSurrogateId, CancellationToken cancellationToken)
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
        await transaction.ExecuteNonQueryAsync(command, cancellationToken);

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
