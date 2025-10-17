// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Channels;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Domain.Models;

namespace Ignixa.Application.Features.Bundle;

/// <summary>
/// Coordinates deferred write operations for bundle processing.
/// Uses a channel-based approach with TaskCompletionSource to enable:
/// 1. Handlers queue writes and immediately return a Task
/// 2. Background batch processor drains channel and writes in batches
/// 3. Handlers' awaits complete when batch processor finishes writing
/// 4. All batches use the same transaction ID for atomicity
///
/// Multi-Tenancy (ADR-2523 Phase 20):
/// - Transaction IDs allocated from Partition 0 (system partition) for global uniqueness
/// - Uses IPartitionStrategy to group resources by partition during batch writes
/// - Supports writing to multiple partitions within same transaction
/// - Commits transaction across all touched partitions
/// </summary>
public class DeferredWriteCoordinator
{
    private readonly Channel<DeferredWriteOperation> _writeChannel;
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly IPartitionStrategy _partitionStrategy;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<DeferredWriteCoordinator> _logger;
    private readonly TransactionId _transactionId;
    private readonly HashSet<int> _touchedPartitions;

    private DeferredWriteCoordinator(
        int channelCapacity,
        IFhirRepositoryFactory repositoryFactory,
        IPartitionStrategy partitionStrategy,
        IHttpContextAccessor httpContextAccessor,
        ILogger<DeferredWriteCoordinator> logger,
        TransactionId transactionId)
    {
        EnsureArg.IsGt(channelCapacity, 0, nameof(channelCapacity));
        EnsureArg.IsNotNull(repositoryFactory, nameof(repositoryFactory));
        EnsureArg.IsNotNull(partitionStrategy, nameof(partitionStrategy));
        EnsureArg.IsNotNull(httpContextAccessor, nameof(httpContextAccessor));
        EnsureArg.IsNotNull(logger, nameof(logger));

        _repositoryFactory = repositoryFactory;
        _partitionStrategy = partitionStrategy;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _transactionId = transactionId;
        _touchedPartitions = new HashSet<int>();

        // Create bounded channel with backpressure
        _writeChannel = Channel.CreateBounded<DeferredWriteOperation>(
            new BoundedChannelOptions(channelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait
            });

        _logger.LogDebug(
            "DeferredWriteCoordinator created with capacity {Capacity}, system transaction ID {TransactionId}",
            channelCapacity,
            transactionId);
    }

    /// <summary>
    /// Creates a new DeferredWriteCoordinator instance with a reserved transaction ID.
    /// Multi-Tenancy: Allocates globally unique transaction ID from Partition 0 (system partition).
    /// This ensures transaction IDs are unique across the entire system, not just per tenant.
    /// </summary>
    /// <param name="channelCapacity">Maximum number of pending write operations.</param>
    /// <param name="repositoryFactory">Factory for obtaining partition-specific repositories.</param>
    /// <param name="partitionStrategy">Strategy for determining which partition(s) to write to.</param>
    /// <param name="httpContextAccessor">Accessor for HttpContext (needed to extract tenant context per operation).</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<DeferredWriteCoordinator> CreateAsync(
        int channelCapacity,
        IFhirRepositoryFactory repositoryFactory,
        IPartitionStrategy partitionStrategy,
        IHttpContextAccessor httpContextAccessor,
        ILogger<DeferredWriteCoordinator> logger,
        CancellationToken cancellationToken = default)
    {
        // CRITICAL: Get transaction ID from Partition 0 (system partition)
        // This ensures globally unique transaction IDs across the entire system
        var systemRepository = await repositoryFactory.GetRepositoryAsync(
            SystemConstants.SystemPartitionId,
            cancellationToken);

        // Allocate transaction ID from system partition
        var transactionId = await systemRepository.GetNextTransactionIdAsync(cancellationToken);

        logger.LogDebug(
            "Allocated system transaction ID {TransactionId} from Partition {PartitionId}",
            transactionId,
            SystemConstants.SystemPartitionId);

        return new DeferredWriteCoordinator(
            channelCapacity,
            repositoryFactory,
            partitionStrategy,
            httpContextAccessor,
            logger,
            transactionId);
    }

    /// <summary>
    /// Queues a write operation and returns a Task that completes when the write finishes.
    /// </summary>
    /// <param name="wrapper">The resource wrapper containing all resource data.</param>
    /// <param name="entryIndex">The entry index (for logging). Defaults to 0 when called from handler context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task that completes with ResourceKey when write finishes.</returns>
    public async Task<ResourceKey> QueueWriteAsync(
        ResourceWrapper wrapper,
        int entryIndex = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wrapper);

        // Create TaskCompletionSource with RunContinuationsAsynchronously flag
        // This is CRITICAL: Without this flag, continuations run on the batch processor thread,
        // causing deadlocks and poor performance.
        var tcs = new TaskCompletionSource<ResourceKey>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var operation = new DeferredWriteOperation
        {
            Wrapper = wrapper,
            CompletionSource = tcs,
            EntryIndex = entryIndex
        };

        _logger.LogDebug(
            "Queuing write for entry {EntryIndex}: {ResourceType}/{ResourceId}",
            entryIndex,
            wrapper.ResourceType,
            wrapper.ResourceId);

        // Write to channel (may block if channel is full - provides backpressure)
        await _writeChannel.Writer.WriteAsync(operation, cancellationToken);

        // Return the Task - handler awaits this, it completes when batch processor writes
        return await tcs.Task;
    }

    /// <summary>
    /// Processes a batch of queued write operations.
    /// Called by background batch processor task.
    /// Multi-Tenancy: Groups operations by partition using IPartitionStrategy and writes to each partition.
    /// </summary>
    /// <param name="batchSize">Maximum number of operations to process in one batch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of exceptions that occurred during processing (empty if all succeeded).</returns>
    public async Task<List<Exception>> ProcessBatchAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        EnsureArg.IsGt(batchSize, 0, nameof(batchSize));

        var batch = new List<DeferredWriteOperation>();
        var errors = new List<Exception>();

        // Read up to batchSize operations from channel
        // Wait for at least one operation to be available
        if (!await _writeChannel.Reader.WaitToReadAsync(cancellationToken))
        {
            return errors; // Channel completed with no data
        }

        // Read all currently available operations (up to batchSize)
        while (batch.Count < batchSize && _writeChannel.Reader.TryRead(out var operation))
        {
            batch.Add(operation);
        }

        if (batch.Count == 0)
        {
            return errors; // No operations to process
        }

        _logger.LogDebug("Processing batch of {Count} write operations", batch.Count);

        // Group operations by partition using IPartitionStrategy
        var operationsByPartition = new Dictionary<int, List<(DeferredWriteOperation Operation, int Index)>>();

        try
        {
            // Extract tenant context from HttpContext (set by TenantResolutionMiddleware)
            var httpContext = _httpContextAccessor.HttpContext
                ?? throw new InvalidOperationException("HttpContext is null - bundle processing requires tenant context");

            if (!httpContext.Items.TryGetValue("TenantId", out var tenantIdObj) || tenantIdObj is not int tenantId)
            {
                throw new InvalidOperationException("TenantId not found in HttpContext.Items");
            }

            var tenantConfig = httpContext.Items["TenantConfiguration"] as TenantConfiguration;

            var partitionContext = new PartitionResolutionContext
            {
                TenantId = tenantId,
                TenantConfiguration = tenantConfig
            };

            // Determine partition for each operation
            for (int i = 0; i < batch.Count; i++)
            {
                var operation = batch[i];

                // Use partition strategy to determine where this resource should be written
                var partition = _partitionStrategy.DetermineWritePartition(partitionContext, operation.Wrapper.Resource);

                // Validate single partition (writes always go to one partition)
                if (partition.PartitionIds.Count != 1)
                {
                    _logger.LogError(
                        "Write operation for entry {EntryIndex} ({ResourceType}/{ResourceId}) requires exactly 1 partition, received {Count} partition IDs",
                        operation.EntryIndex,
                        operation.Wrapper.ResourceType,
                        operation.Wrapper.ResourceId,
                        partition.PartitionIds.Count);
                    throw new InvalidOperationException(
                        $"Write operation requires exactly 1 partition, received {partition.PartitionIds.Count} partition IDs");
                }

                int partitionId = partition.PartitionIds[0];

                // Group by partition
                if (!operationsByPartition.ContainsKey(partitionId))
                {
                    operationsByPartition[partitionId] = new List<(DeferredWriteOperation, int)>();
                }

                operationsByPartition[partitionId].Add((operation, i));
            }

            _logger.LogDebug(
                "Grouped {TotalCount} operations into {PartitionCount} partition(s): {Partitions}",
                batch.Count,
                operationsByPartition.Count,
                string.Join(", ", operationsByPartition.Select(kvp => $"Partition {kvp.Key} ({kvp.Value.Count} ops)")));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to group operations by partition");

            // Fail all operations
            foreach (var operation in batch)
            {
                operation.CompletionSource.SetException(ex);
            }

            errors.Add(ex);
            return errors;
        }

        // Write to each partition's repository
        var allResults = new ResourceKey[batch.Count];

        foreach (var (partitionId, operations) in operationsByPartition)
        {
            try
            {
                // Get partition-specific repository
                var repository = await _repositoryFactory.GetRepositoryAsync(partitionId, cancellationToken);

                // Convert to batch write operations
                var batchOperations = operations
                    .Select(tuple => (
                        tuple.Operation.Wrapper.ResourceType,
                        tuple.Operation.Wrapper.ResourceId,
                        tuple.Operation.Wrapper.Resource,
                        tuple.Operation.Wrapper.SearchIndices ?? (IReadOnlyList<object>)Array.Empty<object>()
                    ))
                    .ToList();

                _logger.LogDebug(
                    "Writing batch of {Count} resources to Partition {PartitionId}: {Resources}",
                    batchOperations.Count,
                    partitionId,
                    string.Join(", ", batchOperations.Select(op => $"{op.ResourceType}/{op.ResourceId}")));

                // Execute batch write using the coordinator's transaction ID
                var results = await repository.BatchWriteAsync(_transactionId, batchOperations, cancellationToken);

                // Track partition
                _touchedPartitions.Add(partitionId);

                // Store results in correct positions
                for (int i = 0; i < operations.Count; i++)
                {
                    var (operation, originalIndex) = operations[i];
                    var result = results[i];

                    allResults[originalIndex] = result;

                    _logger.LogDebug(
                        "Write completed for entry {EntryIndex}: {ResourceType}/{ResourceId} version {VersionId} (Partition {PartitionId})",
                        operation.EntryIndex,
                        result.ResourceType,
                        result.Id,
                        result.VersionId,
                        partitionId);
                }

                _logger.LogDebug(
                    "Batch write to Partition {PartitionId} complete: {Count} resources written",
                    partitionId,
                    operations.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Batch write failed for Partition {PartitionId} ({Count} resources)",
                    partitionId,
                    operations.Count);

                // Fail all operations in this partition
                foreach (var (operation, _) in operations)
                {
                    operation.CompletionSource.SetException(ex);
                }

                errors.Add(ex);
            }
        }

        // Complete all successful TaskCompletionSources
        for (int i = 0; i < batch.Count; i++)
        {
            var operation = batch[i];
            var result = allResults[i];

            if (result != null && !operation.CompletionSource.Task.IsCompleted)
            {
                operation.CompletionSource.SetResult(result);
            }
        }

        if (errors.Count == 0)
        {
            _logger.LogDebug(
                "Batch processing complete: {Count} resources written successfully across {PartitionCount} partition(s)",
                batch.Count,
                operationsByPartition.Count);
        }

        return errors;
    }

    /// <summary>
    /// Signals that no more writes will be queued.
    /// Call this after all entries have been queued.
    /// </summary>
    public void CompleteWrites()
    {
        _writeChannel.Writer.Complete();
        _logger.LogDebug("Write channel completed (no more writes will be queued)");
    }

    /// <summary>
    /// Signals that no more writes will be queued due to an error.
    /// </summary>
    /// <param name="exception">The exception that caused the failure.</param>
    public void CompleteWrites(Exception exception)
    {
        EnsureArg.IsNotNull(exception, nameof(exception));

        _writeChannel.Writer.Complete(exception);
        _logger.LogWarning(exception, "Write channel completed with error");
    }

    /// <summary>
    /// Gets the number of pending write operations in the channel.
    /// Useful for diagnostics and monitoring.
    /// </summary>
    public int PendingOperationCount => _writeChannel.Reader.Count;

    /// <summary>
    /// Gets whether the write channel has been completed (no more writes will be queued).
    /// Used by background processors to determine when to exit.
    /// </summary>
    public bool IsCompleted => _writeChannel.Reader.Completion.IsCompleted;

    /// <summary>
    /// Waits for data to become available in the channel or for the channel to complete.
    /// Returns true when data is available, false when channel is completed with no data.
    /// </summary>
    public async Task<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
    {
        return await _writeChannel.Reader.WaitToReadAsync(cancellationToken);
    }

    /// <summary>
    /// Commits the transaction by renaming the lock file to committed file.
    /// Should be called after all batches are complete and writes are finished.
    /// Multi-Tenancy: Commits transaction across all partitions that were written to during bundle processing.
    /// </summary>
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_touchedPartitions.Count == 0)
        {
            _logger.LogWarning(
                "Transaction {TransactionId} has no partitions to commit (no writes were processed)",
                _transactionId);
            return;
        }

        _logger.LogDebug(
            "Committing transaction {TransactionId} across {Count} partition(s): {Partitions}",
            _transactionId,
            _touchedPartitions.Count,
            string.Join(", ", _touchedPartitions));

        // Commit transaction on all touched partitions
        var commitErrors = new List<Exception>();

        foreach (var partitionId in _touchedPartitions)
        {
            try
            {
                var repository = await _repositoryFactory.GetRepositoryAsync(partitionId, cancellationToken);
                await repository.CommitTransactionAsync(_transactionId, cancellationToken);

                _logger.LogDebug(
                    "Transaction {TransactionId} committed successfully on Partition {PartitionId}",
                    _transactionId,
                    partitionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to commit transaction {TransactionId} on Partition {PartitionId}",
                    _transactionId,
                    partitionId);
                commitErrors.Add(ex);
            }
        }

        if (commitErrors.Count > 0)
        {
            throw new AggregateException(
                $"Failed to commit transaction {_transactionId} on {commitErrors.Count} partition(s)",
                commitErrors);
        }

        _logger.LogInformation(
            "Transaction {TransactionId} committed successfully across {Count} partition(s): {Partitions}",
            _transactionId,
            _touchedPartitions.Count,
            string.Join(", ", _touchedPartitions));
    }

    /// <summary>
    /// Gets the transaction ID for this coordinator.
    /// </summary>
    public TransactionId TransactionId => _transactionId;
}
