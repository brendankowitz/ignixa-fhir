// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ignixa.DataLayer.SqlEntityFramework.Compression;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.SourceNodeSerialization;
using Ignixa.SourceNodeSerialization.Models;
using Ignixa.SourceNodeSerialization.SourceNodes;

namespace Ignixa.DataLayer.SqlEntityFramework;

/// <summary>
/// Entity Framework Core implementation of IFhirRepository using Microsoft FHIR Server legacy schema.
/// Supports multi-tenancy with one database per tenant (isolation mode).
/// </summary>
public class SqlEntityFrameworkRepository : IFhirRepository
{
    private readonly FhirDbContext _context;
    private readonly GzipResourceCompressor _compressor;
    private readonly SearchIndexWriter _searchIndexWriter;
    private readonly ILogger<SqlEntityFrameworkRepository> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlEntityFrameworkRepository"/> class.
    /// </summary>
    /// <param name="context">The EF Core DbContext.</param>
    /// <param name="compressor">The Gzip compressor for RawResource storage.</param>
    /// <param name="searchIndexWriter">The search index writer for indexing resources.</param>
    /// <param name="logger">Logger instance.</param>
    public SqlEntityFrameworkRepository(
        FhirDbContext context,
        GzipResourceCompressor compressor,
        SearchIndexWriter searchIndexWriter,
        ILogger<SqlEntityFrameworkRepository> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _compressor = compressor ?? throw new ArgumentNullException(nameof(compressor));
        _searchIndexWriter = searchIndexWriter ?? throw new ArgumentNullException(nameof(searchIndexWriter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async ValueTask<SearchEntryResult?> GetAsync(ResourceKey key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        _logger.LogDebug("Getting resource {ResourceType}/{ResourceId}", key.ResourceType, key.Id);

        // Get ResourceTypeId
        var resourceTypeId = await GetOrCreateResourceTypeIdAsync(key.ResourceType, ct);

        // Query for the resource
        ResourceEntity? entity;

        if (key.VersionId != null && int.TryParse(key.VersionId, out var version))
        {
            // Get specific version
            entity = await _context.Resources
                .Where(r => r.ResourceTypeId == resourceTypeId
                    && r.ResourceId == key.Id
                    && r.Version == version)
                .Include(x => x.Transaction)
                .FirstOrDefaultAsync(ct);
        }
        else
        {
            // Get current version (IsHistory = false)
            entity = await _context.Resources
                .Where(r => r.ResourceTypeId == resourceTypeId
                    && r.ResourceId == key.Id
                    && !r.IsHistory)
                .Include(x => x.Transaction)
                .OrderByDescending(r => r.Version)
                .FirstOrDefaultAsync(ct);
        }

        if (entity == null)
        {
            _logger.LogDebug("Resource not found: {ResourceType}/{ResourceId}", key.ResourceType, key.Id);
            return null;
        }

        // NOTE: Do NOT filter out deleted resources here - return them with IsDeleted=true
        // The API layer (FhirEndpoints.HandleGetResource) will check IsDeleted and return 410 Gone
        // This allows proper FHIR-compliant behavior: 404 = never existed, 410 = deleted

        // Decompress RawResource to bytes (no parsing!)
        // Return SearchEntryResult with raw bytes for zero-copy serialization
        var result = new SearchEntryResult(
            ResourceType: key.ResourceType,
            ResourceId: entity.ResourceId,
            VersionId: entity.Version.ToString(),
            LastModified: entity.Transaction?.CreateDate ?? DateTimeOffset.UtcNow,
            ResourceBytes: _compressor.DecompressBytes(entity.RawResource))
        {
            IsDeleted = entity.IsDeleted,
            TenantId = key.TenantId,
        };

        _logger.LogDebug("Retrieved resource {ResourceType}/{ResourceId} version {Version}", key.ResourceType, key.Id, entity.Version);

        return result;
    }

    /// <inheritdoc/>
    public async ValueTask<ResourceKey> CreateOrUpdateAsync(ResourceWrapper resource, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrEmpty(resource.ResourceType);
        ArgumentException.ThrowIfNullOrEmpty(resource.ResourceId);

        if (resource.Resource == null)
        {
            throw new ArgumentException("Resource is required", nameof(resource));
        }

        _logger.LogDebug("Creating/updating resource {ResourceType}/{ResourceId}", resource.ResourceType, resource.ResourceId);

        // Use shared helper to create resource entity
        var (entity, newVersion) = await CreateResourceEntityAsync(
            resource.ResourceType,
            resource.ResourceId,
            resource.Resource,
            resource.SearchIndices ?? Array.Empty<object>(),
            resource.Request.Method,
            transactionId: null,
            ct);

        // Save changes immediately (standalone operation)
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Created resource {ResourceType}/{ResourceId} version {Version}", resource.ResourceType, resource.ResourceId, newVersion);

        return new ResourceKey(resource.ResourceType, resource.ResourceId, newVersion.ToString(), resource.TenantId);
    }

    /// <inheritdoc/>
    public async ValueTask<ResourceKey?> DeleteAsync(
        ResourceKey key,
        ResourceRequest request,
        TransactionId? transactionId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogDebug("Deleting resource {ResourceType}/{ResourceId}", key.ResourceType, key.Id);

        // Get ResourceTypeId
        var resourceTypeId = await GetOrCreateResourceTypeIdAsync(key.ResourceType, ct);

        // Find current version (IsHistory = false)
        var currentEntity = await _context.Resources
            .Where(r => r.ResourceTypeId == resourceTypeId
                && r.ResourceId == key.Id
                && !r.IsHistory)
            .Include(r => r.Transaction)
            .OrderByDescending(r => r.Version)
            .FirstOrDefaultAsync(ct);

        if (currentEntity == null)
        {
            // Resource never existed - return null (404 Not Found)
            _logger.LogWarning(
                "Cannot delete {ResourceType}/{ResourceId}: resource never existed",
                key.ResourceType,
                key.Id);
            return null;
        }

        if (currentEntity.IsDeleted)
        {
            // Idempotency: Already deleted, return existing deleted version
            _logger.LogDebug(
                "Resource {ResourceType}/{ResourceId} already deleted at version {Version} (idempotent)",
                key.ResourceType,
                key.Id,
                currentEntity.Version);

            return new ResourceKey(
                key.ResourceType,
                key.Id,
                currentEntity.Version.ToString(),
                key.TenantId);
        }

        // Create new deleted version
        int newVersion = currentEntity.Version + 1;

        // Mark old version as history
        currentEntity.IsHistory = true;
        // TODO: Set HistoryTransactionId if needed

        // Create minimal tombstone JSON (FHIR spec: id and meta only)
        var tombstoneJsonNode = new ResourceJsonNode
        {
            ResourceType = key.ResourceType,
            Id = key.Id,
            Meta = new MetaJsonNode
            {
                VersionId = newVersion.ToString(),
                LastUpdated = DateTimeOffset.UtcNow
            }
        };

        // Compress minimal tombstone
        var compressedTombstone = _compressor.SerializeAndCompress(tombstoneJsonNode);

        // Create new deleted version entity
        var deletedEntity = new ResourceEntity
        {
            ResourceTypeId = resourceTypeId,
            ResourceId = key.Id,
            Version = newVersion,
            IsHistory = false, // This is now the current version
            ResourceSurrogateId = await GetNextSurrogateIdAsync(ct),
            IsDeleted = true, // Mark as deleted
            RequestMethod = "DELETE",
            RawResource = compressedTombstone,
            IsRawResourceMetaSet = true, // Meta is set in tombstone
            SearchParamHash = null, // Deleted resources have no search indices
            TransactionId = transactionId?.Value,
            HistoryTransactionId = null,
        };

        _context.Resources.Add(deletedEntity);

        // Save changes immediately if not part of a transaction
        if (!transactionId.HasValue)
        {
            await _context.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "Created tombstone for {ResourceType}/{ResourceId} version {Version}",
            key.ResourceType,
            key.Id,
            newVersion);

        return new ResourceKey(key.ResourceType, key.Id, newVersion.ToString(), key.TenantId);
    }

    /// <inheritdoc/>
    public async ValueTask<TransactionId> GetNextTransactionIdAsync(CancellationToken ct = default)
    {
        // Allocate surrogate ID range for this transaction
        var firstId = await GetNextSurrogateIdAsync(ct);
        var lastId = firstId + 999; // Reserve 1000 IDs

        var transaction = new TransactionEntity
        {
            SurrogateIdRangeFirstValue = firstId,
            SurrogateIdRangeLastValue = lastId,
            IsCompleted = false,
            IsSuccess = false,
            IsVisible = false,
            IsHistoryMoved = false,
            CreateDate = DateTime.UtcNow,
            HeartbeatDate = DateTime.UtcNow,
            IsControlledByClient = true,
        };

        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync(ct);

        _logger.LogDebug("Allocated transaction ID range: {FirstId}-{LastId}", firstId, lastId);

        return new TransactionId(firstId);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ResourceKey>> BatchWriteAsync(
        TransactionId transactionId,
        IReadOnlyList<(string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes)> operations,
        CancellationToken ct = default)
    {
        // Note: transactionId is a struct, ArgumentNullException.ThrowIfNull doesn't make sense
        ArgumentNullException.ThrowIfNull(operations);

        _logger.LogDebug("Batch writing {Count} resources for transaction {TransactionId}", operations.Count, transactionId.Value);

        var results = new List<ResourceKey>();

        foreach (var (resourceType, resourceId, resource, searchIndexes) in operations)
        {
            // Use shared helper to create resource entity
            var (entity, newVersion) = await CreateResourceEntityAsync(
                resourceType,
                resourceId,
                resource,
                searchIndexes,
                requestMethod: "POST",
                transactionId: transactionId.Value,
                ct);

            results.Add(new ResourceKey(resourceType, resourceId, newVersion.ToString(), null));
        }

        // Note: SaveChangesAsync is NOT called here - deferred until CommitTransactionAsync
        return results;
    }

    /// <inheritdoc/>
    public async ValueTask CommitTransactionAsync(TransactionId transactionId, CancellationToken ct = default)
    {
        // Note: transactionId is a struct, ArgumentNullException.ThrowIfNull doesn't make sense
        _logger.LogDebug("Committing transaction {TransactionId}", transactionId.Value);

        // Find transaction entity
        var transaction = await _context.Transactions
            .FirstOrDefaultAsync(t => t.SurrogateIdRangeFirstValue == transactionId.Value, ct);

        if (transaction == null)
        {
            throw new InvalidOperationException($"Transaction {transactionId.Value} not found");
        }

        // Mark as completed and visible
        transaction.IsCompleted = true;
        transaction.IsSuccess = true;
        transaction.IsVisible = true;
        transaction.EndDate = DateTime.UtcNow;
        transaction.VisibleDate = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Committed transaction {TransactionId}", transactionId.Value);
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<TransactionId>> GetStalledTransactionsAsync(
        TimeSpan stallThreshold,
        CancellationToken ct = default)
    {
        var threshold = DateTime.UtcNow - stallThreshold;

        _logger.LogDebug(
            "Querying for stalled transactions (IsCompleted = false, HeartbeatDate < {Threshold})",
            threshold);

        // Query TransactionEntity table for incomplete transactions with old heartbeat dates
        var stalledTransactions = await _context.Transactions
            .Where(t => !t.IsCompleted && t.HeartbeatDate < threshold)
            .Select(t => new TransactionId(t.SurrogateIdRangeFirstValue))
            .ToListAsync(ct);

        if (stalledTransactions.Count > 0)
        {
            _logger.LogWarning(
                "Found {Count} stalled transactions in database (threshold: {Threshold})",
                stalledTransactions.Count,
                threshold);

            foreach (var txId in stalledTransactions)
            {
                _logger.LogWarning("Stalled transaction: {TransactionId}", txId.Value);
            }
        }
        else
        {
            _logger.LogDebug("No stalled transactions found");
        }

        return stalledTransactions;
    }

    // Helper methods

    /// <summary>
    /// Creates a new ResourceEntity for a resource, handling versioning, compression, and search indexing.
    /// This method consolidates the shared logic between CreateOrUpdateAsync and BatchWriteAsync.
    /// Does NOT call SaveChangesAsync - caller is responsible for persisting changes.
    /// </summary>
    /// <returns>Tuple of (created entity, new version number)</returns>
    private async Task<(ResourceEntity entity, int newVersion)> CreateResourceEntityAsync(
        string resourceType,
        string resourceId,
        ResourceJsonNode resource,
        IReadOnlyList<object> searchIndexes,
        string requestMethod,
        long? transactionId,
        CancellationToken ct)
    {
        // Get ResourceTypeId
        var resourceTypeId = await GetOrCreateResourceTypeIdAsync(resourceType, ct);

        // Get current version (if exists)
        var currentEntity = await _context.Resources
            .Where(r => r.ResourceTypeId == resourceTypeId
                && r.ResourceId == resourceId
                && !r.IsHistory)
            .OrderByDescending(r => r.ResourceSurrogateId)
            .FirstOrDefaultAsync(ct);

        int newVersion = currentEntity?.Version + 1 ?? 1;

        // Mark old version as history (if exists)
        if (currentEntity != null)
        {
            currentEntity.IsHistory = true;
            // TODO: Set HistoryTransactionId
        }

        // Compress JSON
        var compressedData = _compressor.SerializeAndCompress(resource);

        // Create new version
        var newEntity = new ResourceEntity
        {
            ResourceTypeId = resourceTypeId,
            ResourceId = resourceId,
            Version = newVersion,
            IsHistory = false,
            ResourceSurrogateId = await GetNextSurrogateIdAsync(ct),
            IsDeleted = false,
            RequestMethod = requestMethod,
            RawResource = compressedData,
            IsRawResourceMetaSet = false, // TODO: Parse JSON to check if meta is set
            SearchParamHash = null, // TODO: Calculate search param hash
            TransactionId = transactionId,
            HistoryTransactionId = null,
        };

        _context.Resources.Add(newEntity);

        // Write search indices
        if (searchIndexes.Count > 0)
        {
            await _searchIndexWriter.WriteSearchIndicesAsync(
                resourceTypeId,
                newEntity.ResourceSurrogateId,
                searchIndexes,
                isHistory: false);
        }

        return (newEntity, newVersion);
    }

    private async ValueTask<short> GetOrCreateResourceTypeIdAsync(string resourceType, CancellationToken ct)
    {
        var entity = await _context.ResourceTypes
            .FirstOrDefaultAsync(rt => rt.Name == resourceType, ct);

        if (entity != null)
        {
            return entity.ResourceTypeId;
        }

        // Create new resource type
        var newEntity = new ResourceTypeEntity
        {
            Name = resourceType,
        };

        _context.ResourceTypes.Add(newEntity);
        await _context.SaveChangesAsync(ct);

        return newEntity.ResourceTypeId;
    }

    private async ValueTask<long> GetNextSurrogateIdAsync(CancellationToken ct)
    {
        // Use SQL Server SEQUENCE for thread-safe, high-performance ID generation
        // Matches legacy stored procedure pattern from MergeResourcesBeginTransaction

        // Get next value from sequence (CACHE 1000000 for optimal performance)
        // Use FromSqlRaw to execute the sequence query directly
        using var command = _context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT NEXT VALUE FOR dbo.ResourceSurrogateIdUniquifierSequence";

        await _context.Database.OpenConnectionAsync(ct);
        try
        {
            var result = await command.ExecuteScalarAsync(ct);
            var sequenceValue = Convert.ToInt32(result);

            // Apply composite ID formula (matches legacy pattern):
            // surrogateId = (milliseconds since 0001-01-01) * 80000 + sequenceValue
            // High-order bits: timestamp (ensures time-ordered IDs)
            // Low-order bits: sequence (0-79999, cycles for high throughput)
            var millisecondsSinceMinValue = (long)(DateTimeOffset.UtcNow - DateTimeOffset.MinValue).TotalMilliseconds;
            var surrogateId = millisecondsSinceMinValue * 80000 + sequenceValue;

            return surrogateId;
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
    }

    public async IAsyncEnumerable<SearchEntryResult> GetResourceHistoryAsync(
        ResourceKey key,
        HistoryQueryParameters parameters,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(parameters);

        // Validate parameters
        parameters = parameters.Validate();

        _logger.LogDebug(
            "Getting history for resource {ResourceType}/{ResourceId} (count={Count}, offset={Offset})",
            key.ResourceType,
            key.Id,
            parameters.Count,
            parameters.Offset);

        // Get ResourceTypeId
        var resourceTypeId = await GetOrCreateResourceTypeIdAsync(key.ResourceType, ct);

        // Query all versions of this resource (both current and historical)
        var query = _context.Resources
            .Where(r => r.ResourceTypeId == resourceTypeId && r.ResourceId == key.Id)
            .Include(r => r.Transaction);

        // Stream results incrementally
        await foreach (var result in ExecuteHistoryQueryAsync(query, parameters, ct))
        {
            yield return result;
        }
    }

    public async IAsyncEnumerable<SearchEntryResult> GetTypeHistoryAsync(
        string resourceType,
        int tenantId,
        HistoryQueryParameters parameters,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(resourceType);
        ArgumentNullException.ThrowIfNull(parameters);

        // Validate parameters
        parameters = parameters.Validate();

        _logger.LogDebug(
            "Getting history for resource type {ResourceType} (count={Count}, offset={Offset})",
            resourceType,
            parameters.Count,
            parameters.Offset);

        // Get ResourceTypeId
        var resourceTypeId = await GetOrCreateResourceTypeIdAsync(resourceType, ct);

        // Query all versions of all resources of this type
        var query = _context.Resources
            .Where(r => r.ResourceTypeId == resourceTypeId)
            .Include(r => r.Transaction);

        // Stream results incrementally
        await foreach (var result in ExecuteHistoryQueryAsync(query, parameters, ct))
        {
            yield return result;
        }
    }

    public async IAsyncEnumerable<SearchEntryResult> GetSystemHistoryAsync(
        int tenantId,
        HistoryQueryParameters parameters,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // Validate parameters
        parameters = parameters.Validate();

        _logger.LogDebug(
            "Getting system-wide history (count={Count}, offset={Offset})",
            parameters.Count,
            parameters.Offset);

        // Query all resources across all types
        var query = _context.Resources
            .Include(r => r.Transaction)
            .Include(r => r.ResourceType);

        // Stream results incrementally
        await foreach (var result in ExecuteHistoryQueryAsync(query, parameters, ct))
        {
            yield return result;
        }
    }

    private async IAsyncEnumerable<SearchEntryResult> ExecuteHistoryQueryAsync(
        IQueryable<ResourceEntity> query,
        HistoryQueryParameters parameters,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // Apply timestamp filtering (_since and _until)
        if (parameters.Since.HasValue)
        {
            var sinceUtc = parameters.Since.Value.UtcDateTime;
            query = query.Where(r => r.Transaction != null && r.Transaction.CreateDate >= sinceUtc);
        }

        if (parameters.Until.HasValue)
        {
            var untilUtc = parameters.Until.Value.UtcDateTime;
            query = query.Where(r => r.Transaction != null && r.Transaction.CreateDate <= untilUtc);
        }

        // Apply sorting by transaction creation date
        query = parameters.Sort == HistorySortOrder.Ascending
            ? query.OrderBy(r => r.Transaction!.CreateDate).ThenBy(r => r.ResourceSurrogateId)
            : query.OrderByDescending(r => r.Transaction!.CreateDate).ThenByDescending(r => r.ResourceSurrogateId);

        // Apply pagination
        query = query.Skip(parameters.Offset).Take(parameters.Count);

        // Stream results incrementally using AsAsyncEnumerable
        await foreach (var entity in query.AsAsyncEnumerable().WithCancellation(ct))
        {
            SearchEntryResult? result = null;

            try
            {
                // Decompress resource bytes
                var resourceBytes = _compressor.DecompressBytes(entity.RawResource);

                // Determine resource type name (may need to load ResourceType entity if not included)
                var resourceTypeName = entity.ResourceType?.Name;
                if (string.IsNullOrEmpty(resourceTypeName))
                {
                    var resourceType = await _context.ResourceTypes
                        .Where(rt => rt.ResourceTypeId == entity.ResourceTypeId)
                        .FirstOrDefaultAsync(ct);
                    resourceTypeName = resourceType?.Name ?? "Unknown";
                }

                result = new SearchEntryResult(
                    ResourceType: resourceTypeName,
                    ResourceId: entity.ResourceId,
                    VersionId: entity.Version.ToString(),
                    LastModified: entity.Transaction?.CreateDate.ToUniversalTime() ?? DateTimeOffset.UtcNow,
                    ResourceBytes: resourceBytes)
                {
                    IsDeleted = entity.IsDeleted,
                    Request = new ResourceRequest(entity.RequestMethod ?? "PUT", $"{resourceTypeName}/{entity.ResourceId}")
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to deserialize resource {ResourceId} version {Version}",
                    entity.ResourceId,
                    entity.Version);
            }

            if (result != null)
            {
                yield return result;
            }
        }
    }
}
