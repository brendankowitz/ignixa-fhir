// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sparky.DataLayer.LegacySqlEF.Compression;
using Sparky.DataLayer.LegacySqlEF.Entities;
using Sparky.DataLayer.LegacySqlEF.Indexing;
using Sparky.Domain.Abstractions;
using Sparky.Domain.ElementModel;
using Sparky.Domain.Models;
using Sparky.SourceNodeSerialization;
using Sparky.SourceNodeSerialization.SourceNodes.Models;

namespace Sparky.DataLayer.LegacySqlEF;

/// <summary>
/// Entity Framework Core implementation of IFhirRepository using Microsoft FHIR Server legacy schema.
/// Supports multi-tenancy with one database per tenant (isolation mode).
/// </summary>
public class LegacySqlEfRepository : IFhirRepository
{
    private readonly FhirDbContext _context;
    private readonly GzipResourceCompressor _compressor;
    private readonly SearchIndexWriter _searchIndexWriter;
    private readonly ILogger<LegacySqlEfRepository> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LegacySqlEfRepository"/> class.
    /// </summary>
    /// <param name="context">The EF Core DbContext.</param>
    /// <param name="compressor">The Gzip compressor for RawResource storage.</param>
    /// <param name="searchIndexWriter">The search index writer for indexing resources.</param>
    /// <param name="logger">Logger instance.</param>
    public LegacySqlEfRepository(
        FhirDbContext context,
        GzipResourceCompressor compressor,
        SearchIndexWriter searchIndexWriter,
        ILogger<LegacySqlEfRepository> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _compressor = compressor ?? throw new ArgumentNullException(nameof(compressor));
        _searchIndexWriter = searchIndexWriter ?? throw new ArgumentNullException(nameof(searchIndexWriter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async ValueTask<ResourceWrapper?> GetAsync(ResourceKey key, CancellationToken ct = default)
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

        // Check if deleted
        if (entity.IsDeleted)
        {
            _logger.LogDebug("Resource is deleted: {ResourceType}/{ResourceId}", key.ResourceType, key.Id);
            return null;
        }

        // Decompress RawResource
        var json = _compressor.Decompress(entity.RawResource);

        // Create ResourceWrapper
        // Note: We're using a simplified approach - storing JSON and creating a placeholder ResourceRequest
        var request = new ResourceRequest(
            Method: entity.RequestMethod ?? "GET",
            Url: $"{key.ResourceType}/{key.Id}");

        // TODO: Parse JSON to create ISourceNode
        // For now, we'll create a minimal wrapper with just the required fields
        // In production, we'd parse the JSON to create a proper ISourceNode
        var wrapper = new ResourceWrapper(
            ResourceType: key.ResourceType,
            ResourceId: entity.ResourceId,
            VersionId: entity.Version.ToString(),
            LastModified: entity.Transaction?.CreateDate ?? DateTimeOffset.UtcNow, // TODO: Get from transaction or entity metadata
            Resource: ResourceJsonNode.Parse(json).ToSourceNode(),
            Request: request,
            IsDeleted: entity.IsDeleted)
        {
            RawJson = json,
            TenantId = key.TenantId,
        };

        _logger.LogDebug("Retrieved resource {ResourceType}/{ResourceId} version {Version}", key.ResourceType, key.Id, entity.Version);

        return wrapper;
    }

    /// <inheritdoc/>
    public async ValueTask<ResourceKey> CreateOrUpdateAsync(ResourceWrapper resource, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrEmpty(resource.ResourceType);
        ArgumentException.ThrowIfNullOrEmpty(resource.ResourceId);

        if (string.IsNullOrEmpty(resource.RawJson))
        {
            throw new ArgumentException("RawJson is required", nameof(resource));
        }

        _logger.LogDebug("Creating/updating resource {ResourceType}/{ResourceId}", resource.ResourceType, resource.ResourceId);

        // Get ResourceTypeId
        var resourceTypeId = await GetOrCreateResourceTypeIdAsync(resource.ResourceType, ct);

        // Get current version (if exists)
        var currentEntity = await _context.Resources
            .Where(r => r.ResourceTypeId == resourceTypeId
                && r.ResourceId == resource.ResourceId
                && !r.IsHistory)
            .OrderByDescending(r => r.Version)
            .FirstOrDefaultAsync(ct);

        int newVersion = currentEntity?.Version + 1 ?? 1;

        // Mark old version as history (if exists)
        if (currentEntity != null)
        {
            currentEntity.IsHistory = true;
            // TODO: Set HistoryTransactionId
        }

        // Compress JSON
        var compressedData = _compressor.Compress(resource.RawJson);

        // Create new version
        var newEntity = new ResourceEntity
        {
            ResourceTypeId = resourceTypeId,
            ResourceId = resource.ResourceId,
            Version = newVersion,
            IsHistory = false,
            ResourceSurrogateId = await GetNextSurrogateIdAsync(ct),
            IsDeleted = false,
            RequestMethod = resource.Request.Method,
            RawResource = compressedData,
            IsRawResourceMetaSet = false, // TODO: Parse JSON to check if meta is set
            SearchParamHash = null, // TODO: Calculate search param hash
            TransactionId = null, // TODO: Get from transaction context
            HistoryTransactionId = null,
        };

        _context.Resources.Add(newEntity);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Created resource {ResourceType}/{ResourceId} version {Version}", resource.ResourceType, resource.ResourceId, newVersion);

        return new ResourceKey(resource.ResourceType, resource.ResourceId, newVersion.ToString(), resource.TenantId);
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
        IReadOnlyList<(string resourceType, string resourceId, ISourceNode resource, string rawJson, IReadOnlyList<object> searchIndexes)> operations,
        CancellationToken ct = default)
    {
        // Note: transactionId is a struct, ArgumentNullException.ThrowIfNull doesn't make sense
        ArgumentNullException.ThrowIfNull(operations);

        _logger.LogDebug("Batch writing {Count} resources for transaction {TransactionId}", operations.Count, transactionId.Value);

        var results = new List<ResourceKey>();

        foreach (var (resourceType, resourceId, resource, rawJson, searchIndexes) in operations)
        {
            // Get ResourceTypeId
            var resourceTypeId = await GetOrCreateResourceTypeIdAsync(resourceType, ct);

            // Get current version (if exists)
            var currentEntity = await _context.Resources
                .Where(r => r.ResourceTypeId == resourceTypeId
                    && r.ResourceId == resourceId
                    && !r.IsHistory)
                .OrderByDescending(r => r.Version)
                .FirstOrDefaultAsync(ct);

            int newVersion = currentEntity?.Version + 1 ?? 1;

            // Mark old version as history (if exists)
            if (currentEntity != null)
            {
                currentEntity.IsHistory = true;
                // TODO: Set HistoryTransactionId
            }

            // Compress JSON
            var compressedData = _compressor.Compress(rawJson);

            // Create new version
            var newEntity = new ResourceEntity
            {
                ResourceTypeId = resourceTypeId,
                ResourceId = resourceId,
                Version = newVersion,
                IsHistory = false,
                ResourceSurrogateId = await GetNextSurrogateIdAsync(ct),
                IsDeleted = false,
                RequestMethod = "POST",
                RawResource = compressedData,
                IsRawResourceMetaSet = false,
                SearchParamHash = null,
                TransactionId = transactionId.Value,
                HistoryTransactionId = null,
            };

            _context.Resources.Add(newEntity);

            // Write search indices
            if (searchIndexes != null && searchIndexes.Count > 0)
            {
                await _searchIndexWriter.WriteSearchIndicesAsync(
                    resourceTypeId,
                    newEntity.ResourceSurrogateId,
                    searchIndexes,
                    isHistory: false);
            }

            results.Add(new ResourceKey(resourceType, resourceId, newVersion.ToString(), null));
        }

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

    // Helper methods

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
        var sequenceValue = await _context.Database
            .SqlQuery<int>($"SELECT NEXT VALUE FOR dbo.ResourceSurrogateIdUniquifierSequence AS Value")
            .FirstAsync(ct);

        // Apply composite ID formula (matches legacy pattern):
        // surrogateId = (milliseconds since 0001-01-01) * 80000 + sequenceValue
        // High-order bits: timestamp (ensures time-ordered IDs)
        // Low-order bits: sequence (0-79999, cycles for high throughput)
        var millisecondsSinceMinValue = (long)(DateTimeOffset.UtcNow - DateTimeOffset.MinValue).TotalMilliseconds;
        var surrogateId = millisecondsSinceMinValue * 80000 + sequenceValue;

        return surrogateId;
    }
}
