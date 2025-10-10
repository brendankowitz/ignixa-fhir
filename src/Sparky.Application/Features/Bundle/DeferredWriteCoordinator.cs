// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Channels;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Sparky.Domain.Abstractions;
using Sparky.Domain.Models;

namespace Sparky.Application.Features.Bundle;

/// <summary>
/// Coordinates deferred write operations for bundle processing.
/// Uses a channel-based approach with TaskCompletionSource to enable:
/// 1. Handlers queue writes and immediately return a Task
/// 2. Background batch processor drains channel and writes in batches
/// 3. Handlers' awaits complete when batch processor finishes writing
/// 4. All batches use the same transaction ID for atomicity
/// </summary>
public class DeferredWriteCoordinator
{
    private readonly Channel<DeferredWriteOperation> _writeChannel;
    private readonly IFhirRepository _repository;
    private readonly ILogger<DeferredWriteCoordinator> _logger;
    private readonly TransactionId _transactionId;

    private DeferredWriteCoordinator(
        int channelCapacity,
        IFhirRepository repository,
        ILogger<DeferredWriteCoordinator> logger,
        TransactionId transactionId)
    {
        EnsureArg.IsGt(channelCapacity, 0, nameof(channelCapacity));
        EnsureArg.IsNotNull(repository, nameof(repository));
        EnsureArg.IsNotNull(logger, nameof(logger));

        _repository = repository;
        _logger = logger;
        _transactionId = transactionId;

        // Create bounded channel with backpressure
        _writeChannel = Channel.CreateBounded<DeferredWriteOperation>(
            new BoundedChannelOptions(channelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait
            });

        _logger.LogDebug(
            "DeferredWriteCoordinator created with capacity {Capacity}, transaction ID {TransactionId}",
            channelCapacity,
            transactionId);
    }

    /// <summary>
    /// Creates a new DeferredWriteCoordinator instance with a reserved transaction ID.
    /// </summary>
    public static async Task<DeferredWriteCoordinator> CreateAsync(
        int channelCapacity,
        IFhirRepository repository,
        ILogger<DeferredWriteCoordinator> logger,
        CancellationToken cancellationToken = default)
    {
        // Allocate transaction ID from repository
        var transactionId = await repository.GetNextTransactionIdAsync(cancellationToken);

        return new DeferredWriteCoordinator(channelCapacity, repository, logger, transactionId);
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

        if (wrapper.RawJson == null)
        {
            throw new InvalidOperationException("ResourceWrapper.RawJson must not be null for deferred writes");
        }

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

        // Use batch write API for better performance
        try
        {
            // Convert ResourceWrapper list to batch write operations
            var batchOperations = batch
                .Select(op => (
                    op.Wrapper.ResourceType,
                    op.Wrapper.ResourceId,
                    op.Wrapper.Resource,
                    op.Wrapper.RawJson ?? throw new InvalidOperationException(
                        $"RawJson is null for {op.Wrapper.ResourceType}/{op.Wrapper.ResourceId}")
                ))
                .ToList();

            _logger.LogDebug(
                "Writing batch of {Count} resources: {Resources}",
                batchOperations.Count,
                string.Join(", ", batchOperations.Select(op => $"{op.ResourceType}/{op.ResourceId}")));

            // Execute batch write using the coordinator's transaction ID
            var results = await _repository.BatchWriteAsync(_transactionId, batchOperations, cancellationToken);

            // Complete TaskCompletionSources with results
            for (int i = 0; i < batch.Count; i++)
            {
                var operation = batch[i];
                var result = results[i];

                _logger.LogDebug(
                    "Write completed for entry {EntryIndex}: {ResourceType}/{ResourceId} version {VersionId}",
                    operation.EntryIndex,
                    result.ResourceType,
                    result.Id,
                    result.VersionId);

                // Complete the promise - handler's await now completes with result
                operation.CompletionSource.SetResult(result);
            }

            _logger.LogDebug(
                "Batch processing complete: {Count} resources written successfully",
                batch.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Batch write failed for {Count} resources",
                batch.Count);

            // Fail all TaskCompletionSources since batch write is atomic
            foreach (var operation in batch)
            {
                _logger.LogError(
                    "Write failed for entry {EntryIndex}: {ResourceType}/{ResourceId}",
                    operation.EntryIndex,
                    operation.Wrapper.ResourceType,
                    operation.Wrapper.ResourceId);

                operation.CompletionSource.SetException(ex);
            }

            errors.Add(ex);
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
    /// </summary>
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        // Delegate to repository to commit the transaction
        await _repository.CommitTransactionAsync(_transactionId, cancellationToken);
        _logger.LogInformation("Transaction {TransactionId} committed successfully", _transactionId);
    }

    /// <summary>
    /// Gets the transaction ID for this coordinator.
    /// </summary>
    public TransactionId TransactionId => _transactionId;
}
