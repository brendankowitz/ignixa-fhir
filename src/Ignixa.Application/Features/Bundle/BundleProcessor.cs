// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.Logging;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Serialization.Models;
using System.Text.Json.Nodes;

namespace Ignixa.Application.Features.Bundle;

/// <summary>
/// Main orchestrator for FHIR bundle processing.
/// Coordinates parsing, reference resolution, execution, and response building.
/// Multi-Tenancy: Uses IPartitionStrategy to group resources by partition during batch writes (ADR-2523 Phase 20).
/// </summary>
public class BundleProcessor
{
    private readonly BundleReferencePreProcessor _referencePreProcessor;
    private readonly BundleChannelExecutor _channelExecutor;
    private readonly BundleResponseBuilder _responseBuilder;
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly IPartitionStrategy _partitionStrategy;
    private readonly IFhirRequestContextAccessor _contextAccessor;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<BundleProcessor> _logger;

    public BundleProcessor(
        BundleReferencePreProcessor referencePreProcessor,
        BundleChannelExecutor channelExecutor,
        BundleResponseBuilder responseBuilder,
        IFhirRepositoryFactory repositoryFactory,
        IPartitionStrategy partitionStrategy,
        IFhirRequestContextAccessor contextAccessor,
        ILoggerFactory loggerFactory,
        ILogger<BundleProcessor> logger)
    {
        _referencePreProcessor = EnsureArg.IsNotNull(referencePreProcessor, nameof(referencePreProcessor));
        _channelExecutor = EnsureArg.IsNotNull(channelExecutor, nameof(channelExecutor));
        _responseBuilder = EnsureArg.IsNotNull(responseBuilder, nameof(responseBuilder));
        _repositoryFactory = EnsureArg.IsNotNull(repositoryFactory, nameof(repositoryFactory));
        _partitionStrategy = EnsureArg.IsNotNull(partitionStrategy, nameof(partitionStrategy));
        _contextAccessor = EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
        _loggerFactory = EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));
        _logger = EnsureArg.IsNotNull(logger, nameof(logger));
    }

    /// <summary>
    /// Processes a FHIR bundle (transaction or batch) using two-phase streaming.
    /// Phase 1: Streams entries to parallel consumers until problematic entry detected.
    /// Phase 2: If needed, buffers remaining entries for reference resolution.
    /// </summary>
    /// <param name="entryStream">Stream of bundle entries to process.</param>
    /// <param name="options">Processing options (parallelism, bundle type).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response bundle with entry results.</returns>
    public async Task<BundleJsonNode> ProcessAsync(
        IAsyncEnumerable<BundleEntryContext> entryStream,
        BundleProcessingOptions options,
        CancellationToken cancellationToken)
    {
        EnsureArg.IsNotNull(entryStream, nameof(entryStream));
        EnsureArg.IsNotNull(options, nameof(options));

        _logger.LogInformation("Processing {Type} bundle with two-phase streaming", options.Type);

        var responses = new Dictionary<int, BundleEntryResponse>();
        var bufferedEntries = new List<BundleEntryContext>();
        bool needsBuffering = false;
        int phase1StreamedCount = 0;

        // Phase 1: Stream entries to parallel consumers until we hit a problematic entry
        DeferredWriteCoordinator? phase1Coordinator = null;
        System.Threading.Tasks.Task? phase1BatchProcessor = null;

        try
        {
            // Create coordinator for Phase 1
            // Transaction ID allocated from Partition 0 (system partition)
            // Coordinator uses IPartitionStrategy to group resources by partition
            phase1Coordinator = await DeferredWriteCoordinator.CreateAsync(
                channelCapacity: options.ChannelCapacity,
                repositoryFactory: _repositoryFactory,
                partitionStrategy: _partitionStrategy,
                contextAccessor: _contextAccessor,
                logger: _loggerFactory.CreateLogger<DeferredWriteCoordinator>(),
                cancellationToken: cancellationToken);

            // Start background batch processor for Phase 1
            phase1BatchProcessor = StartBatchProcessor(phase1Coordinator, cancellationToken);

            // Phase 1: Stream entries directly through channel for immediate parallel processing
            var emptyReferenceContext = new ReferenceResolutionContext();

            // Create async enumerable that feeds entries until buffering needed
            var phase1Entries = new List<BundleEntryContext>();

            async IAsyncEnumerable<BundleEntryContext> Phase1EntryStream()
            {
                await foreach (var entry in entryStream.WithCancellation(cancellationToken))
                {
                    // Check if this entry requires buffering
                    if (RequiresBuffering(entry))
                    {
                        needsBuffering = true;
                        bufferedEntries.Add(entry);

                        _logger.LogInformation(
                            "Entry {Index} requires buffering (urn:uuid or conditional ref), switching to Phase 2 after {Count} streamed entries",
                            entry.Index,
                            phase1StreamedCount);

                        // Buffer all remaining entries
                        await foreach (var remaining in entryStream.WithCancellation(cancellationToken))
                        {
                            bufferedEntries.Add(remaining);
                        }
                        yield break; // Stop Phase 1 streaming
                    }

                    // Phase 1: Track and yield this entry for immediate parallel processing
                    phase1Entries.Add(entry);
                    phase1StreamedCount++;
                    yield return entry;
                }
            }

            // Execute Phase 1 entries in parallel via streaming channel
            _logger.LogInformation("Phase 1: Streaming entries for parallel processing");

            var phase1ResponseList = new List<BundleEntryResponse>();
            await foreach (var response in _channelExecutor.ExecuteStreamingAsync(
                Phase1EntryStream(),
                emptyReferenceContext,
                options,
                cancellationToken,
                phase1Coordinator))
            {
                phase1ResponseList.Add(response);
            }

            // Map Phase 1 responses back to their entry indices
            for (int i = 0; i < phase1Entries.Count; i++)
            {
                responses[phase1Entries[i].Index] = phase1ResponseList[i];
            }

            _logger.LogInformation("Phase 1 complete: {Count} entries processed", phase1StreamedCount);

            // Complete Phase 1 writes
            if (phase1Coordinator != null)
            {
                phase1Coordinator.CompleteWrites();
                await phase1BatchProcessor!;

                // Commit Phase 1 transaction
                await phase1Coordinator.CommitAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Phase 1 streaming");
            phase1Coordinator?.CompleteWrites(ex);
            return CreateErrorBundle("Failed to process bundle entries", ex.Message);
        }

        // Phase 2: Process buffered entries if needed
        if (needsBuffering && bufferedEntries.Count > 0)
        {
            _logger.LogInformation(
                "Phase 2: Processing {Count} buffered entries with reference resolution",
                bufferedEntries.Count);

            try
            {
                // Pre-process references
                var referenceContext = _referencePreProcessor.PreProcessReferences(bufferedEntries, options.Type);
                _logger.LogDebug("Pre-processed {Count} reference mappings", referenceContext.Count);

                // Reorder by verb for transactions
                if (options.Type == BundleType.Transaction)
                {
                    bufferedEntries = ReorderByVerb(bufferedEntries);
                    _logger.LogDebug("Reordered transaction entries by verb (DELETE → POST → PUT → PATCH → GET)");
                }

                // Create coordinator for Phase 2
                var phase2Coordinator = await DeferredWriteCoordinator.CreateAsync(
                    channelCapacity: options.ChannelCapacity,
                    repositoryFactory: _repositoryFactory,
                    partitionStrategy: _partitionStrategy,
                    contextAccessor: _contextAccessor,
                    logger: _loggerFactory.CreateLogger<DeferredWriteCoordinator>(),
                    cancellationToken: cancellationToken);

                var phase2BatchProcessor = StartBatchProcessor(phase2Coordinator, cancellationToken);

                // Execute buffered entries via streaming
                var phase2ResponseList = new List<BundleEntryResponse>();
                await foreach (var response in _channelExecutor.ExecuteStreamingAsync(
                    ToAsyncEnumerable(bufferedEntries),
                    referenceContext,
                    options,
                    cancellationToken,
                    phase2Coordinator))
                {
                    phase2ResponseList.Add(response);
                }

                // Map Phase 2 responses back to their entry indices
                for (int i = 0; i < bufferedEntries.Count; i++)
                {
                    responses[bufferedEntries[i].Index] = phase2ResponseList[i];
                }

                // Complete Phase 2 writes
                phase2Coordinator.CompleteWrites();
                await phase2BatchProcessor;

                // Commit Phase 2 transaction
                await phase2Coordinator.CommitAsync(cancellationToken);

                _logger.LogInformation("Phase 2 complete: {Count} buffered entries processed", bufferedEntries.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Phase 2 buffered processing");
                return CreateErrorBundle("Failed to process buffered entries", ex.Message);
            }
        }

        // Check if we processed any entries
        if (responses.Count == 0)
        {
            _logger.LogWarning("Empty bundle received - no entries to process");
            return _responseBuilder.BuildResponse(new List<BundleEntryResponse>(), options.Type);
        }

        // Build final response bundle
        try
        {
            var orderedResponses = responses.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value).ToList();
            var responseBundle = _responseBuilder.BuildResponse(orderedResponses, options.Type);

            _logger.LogInformation(
                "Successfully processed {Type} bundle: Phase 1 ({P1Count} entries), Phase 2 ({P2Count} entries)",
                options.Type,
                phase1StreamedCount,
                bufferedEntries.Count);

            return responseBundle;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build response bundle");
            return CreateErrorBundle("Failed to build response bundle", ex.Message);
        }
    }

    /// <summary>
    /// Checks if an entry requires buffering (contains urn:uuid or conditional references).
    /// </summary>
    private bool RequiresBuffering(BundleEntryContext entry)
    {
        return entry.FullUrl?.StartsWith("urn:uuid:", StringComparison.Ordinal) == true ||
               entry.RequestUrl?.StartsWith("urn:uuid:", StringComparison.Ordinal) == true ||
               entry.RequestUrl?.Contains('?', StringComparison.Ordinal) == true;
    }

    /// <summary>
    /// Processes bundle entries in buffered mode with reference resolution.
    /// Used when urn:uuid or conditional references are detected.
    /// </summary>
    private async Task<BundleJsonNode> ProcessBufferedAsync(
        List<BundleEntryContext> entries,
        BundleProcessingOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Processing {Type} bundle in buffered mode for reference resolution ({Count} entries)",
            options.Type,
            entries.Count);

        // 1. Pre-process references (assign IDs for urn:uuid references)
        ReferenceResolutionContext referenceContext;
        try
        {
            referenceContext = _referencePreProcessor.PreProcessReferences(entries, options.Type);
            _logger.LogDebug("Pre-processed {Count} reference mappings", referenceContext.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pre-process references");
            return CreateErrorBundle("Failed to pre-process references", ex.Message);
        }

        // 2. For transactions: reorder by verb to comply with FHIR spec
        // FHIR requires: DELETE → POST → PUT/PATCH → GET (order-independent outcome)
        if (options.Type == BundleType.Transaction)
        {
            entries = ReorderByVerb(entries);
            _logger.LogDebug("Reordered transaction entries by verb (DELETE → POST → PUT → PATCH → GET)");
        }

        // 3. Execute entries using streaming (now properly ordered)
        var responses = new List<BundleEntryResponse>();
        DeferredWriteCoordinator? coordinator = null;
        System.Threading.Tasks.Task? batchProcessorTask = null;

        try
        {
            // Create coordinator for write batching
            coordinator = await DeferredWriteCoordinator.CreateAsync(
                channelCapacity: options.ChannelCapacity,
                repositoryFactory: _repositoryFactory,
                partitionStrategy: _partitionStrategy,
                contextAccessor: _contextAccessor,
                logger: _loggerFactory.CreateLogger<DeferredWriteCoordinator>(),
                cancellationToken: cancellationToken);

            // Start background batch processor
            batchProcessorTask = StartBatchProcessor(coordinator, cancellationToken);

            // Execute via streaming path
            await foreach (var response in _channelExecutor.ExecuteStreamingAsync(
                ToAsyncEnumerable(entries),
                referenceContext,
                options,
                cancellationToken,
                coordinator))
            {
                responses.Add(response);
            }

            // Complete writes and wait for batch processor
            coordinator.CompleteWrites();
            await batchProcessorTask;

            // Commit transaction (rename lock file → committed file)
            await coordinator.CommitAsync(cancellationToken);

            _logger.LogInformation("All deferred writes committed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute bundle entries");
            coordinator?.CompleteWrites(ex);

            if (options.Type == BundleType.Transaction)
            {
                _logger.LogWarning("Transaction rollback not implemented - partial execution may have occurred");
            }

            return CreateErrorBundle("Failed to execute bundle entries", ex.Message);
        }

        // 4. Build response bundle
        try
        {
            var responseBundle = _responseBuilder.BuildResponse(responses, options.Type);
            _logger.LogInformation(
                "Successfully processed buffered {Type} bundle with {EntryCount} entries",
                options.Type,
                entries.Count);
            return responseBundle;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build response bundle");
            return CreateErrorBundle("Failed to build response bundle", ex.Message);
        }
    }

    /// <summary>
    /// Processes bundle entries in streaming mode (happy path).
    /// No urn:uuid or conditional references - just execute in order.
    /// For transactions: validates entries are pre-sorted by verb.
    /// For batch: executes in parallel.
    /// </summary>
    private async Task<BundleJsonNode> ProcessStreamingAsync(
        IAsyncEnumerable<BundleEntryContext> entryStream,
        BundleProcessingOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Processing {Type} bundle in streaming mode (no references to resolve)",
            options.Type);

        var responses = new List<BundleEntryResponse>();
        DeferredWriteCoordinator? coordinator = null;
        System.Threading.Tasks.Task? batchProcessorTask = null;

        try
        {
            // Create coordinator for write batching
            coordinator = await DeferredWriteCoordinator.CreateAsync(
                channelCapacity: options.ChannelCapacity,
                repositoryFactory: _repositoryFactory,
                partitionStrategy: _partitionStrategy,
                contextAccessor: _contextAccessor,
                logger: _loggerFactory.CreateLogger<DeferredWriteCoordinator>(),
                cancellationToken: cancellationToken);

            // Start background batch processor
            batchProcessorTask = StartBatchProcessor(coordinator, cancellationToken);

            // Execute via streaming (validates verb order for transactions, parallel for batch)
            var emptyReferenceContext = new ReferenceResolutionContext();
            await foreach (var response in _channelExecutor.ExecuteStreamingAsync(
                entryStream,
                emptyReferenceContext,
                options,
                cancellationToken,
                coordinator))
            {
                responses.Add(response);
            }

            // Complete writes and wait for batch processor
            coordinator.CompleteWrites();
            await batchProcessorTask;

            // Commit transaction (rename lock file → committed file)
            await coordinator.CommitAsync(cancellationToken);

            _logger.LogInformation("All deferred writes committed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute streaming bundle entries");
            coordinator?.CompleteWrites(ex);
            return CreateErrorBundle("Failed to execute bundle entries", ex.Message);
        }

        // Build response bundle
        try
        {
            var responseBundle = _responseBuilder.BuildResponse(responses, options.Type);
            _logger.LogInformation(
                "Successfully processed streaming {Type} bundle with {EntryCount} entries",
                options.Type,
                responses.Count);
            return responseBundle;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build response bundle");
            return CreateErrorBundle("Failed to build response bundle", ex.Message);
        }
    }

    /// <summary>
    /// Reorders bundle entries by verb to comply with FHIR transaction processing rules.
    /// Order: DELETE → POST → PUT → PATCH → GET
    /// </summary>
    private List<BundleEntryContext> ReorderByVerb(List<BundleEntryContext> entries)
    {
        return entries
            .OrderBy(e => BundleChannelExecutor.GetVerbPriority(e.HttpVerb))
            .ThenBy(e => e.Index) // Preserve original order within same verb
            .ToList();
    }

    /// <summary>
    /// Starts the background batch processor task for deferred writes.
    /// </summary>
    private System.Threading.Tasks.Task StartBatchProcessor(
        DeferredWriteCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        return System.Threading.Tasks.Task.Run(async () =>
        {
            const int batchSize = 50;
            var allErrors = new List<Exception>();

            _logger.LogDebug("Background batch processor started");

            try
            {
                while (coordinator.PendingOperationCount > 0 ||
                       await WaitForWritesAsync(coordinator, cancellationToken))
                {
                    // Only process batch if there are actually operations queued
                    if (coordinator.PendingOperationCount > 0)
                    {
                        var errors = await coordinator.ProcessBatchAsync(batchSize, cancellationToken);
                        allErrors.AddRange(errors);

                        if (errors.Count > 0)
                        {
                            _logger.LogWarning("Batch processor encountered {ErrorCount} errors", errors.Count);
                        }
                    }
                }

                _logger.LogDebug("Background batch processor completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background batch processor failed");
                throw;
            }
        }, cancellationToken);
    }

    private BundleJsonNode CreateErrorBundle(string message, string details)
    {
        var outcome = new OperationOutcomeJsonNode();
        outcome.Issue.Add(new OperationOutcomeJsonNode.IssueComponent()
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.Processing,
            Diagnostics = $"{message}: {details}"
        });

        var bundle = new BundleJsonNode
        {
            Type = BundleJsonNode.BundleType.TransactionResponse
        };
        bundle.Entry.Add(new BundleComponentJsonNode()
        {
            Response = new BundleComponentResponseJsonNode()
            {
                Status = "500",
                Outcome = outcome
            }
        });
        return bundle;
    }

    /// <summary>
    /// Helper to wait for writes to be available in the coordinator's channel.
    /// Returns false when channel is completed and no more writes will arrive.
    /// </summary>
    private async System.Threading.Tasks.Task<bool> WaitForWritesAsync(DeferredWriteCoordinator coordinator, CancellationToken cancellationToken)
    {
        // Channel completed - only continue if there are still pending operations to process
        if (coordinator.IsCompleted)
        {
            return coordinator.PendingOperationCount > 0;
        }

        // Channel still open - wait for data to become available or channel to complete
        // This properly waits without busy-looping
        try
        {
            // WaitToReadAsync returns true when data is available, false when channel is completed
            return await coordinator.WaitToReadAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Request cancelled - stop processing
            return false;
        }
    }

    /// <summary>
    /// Converts a list to an async enumerable for streaming execution.
    /// </summary>
    private async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }
        await System.Threading.Tasks.Task.CompletedTask; // Suppress async warning
    }

    /// <summary>
    /// Processes a batch bundle with streaming execution (no urn:uuid references).
    /// Returns streaming responses that can be serialized by the API layer.
    /// Manages deferred write coordination and cleanup internally.
    /// </summary>
    /// <param name="entryStream">Stream of bundle entries to process.</param>
    /// <param name="options">Processing options (parallelism, bundle type).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Context containing response stream and cleanup task.</returns>
    public async Task<StreamingBundleContext> ProcessBatchStreamingAsync(
        IAsyncEnumerable<BundleEntryContext> entryStream,
        BundleProcessingOptions options,
        CancellationToken cancellationToken)
    {
        EnsureArg.IsNotNull(entryStream, nameof(entryStream));
        EnsureArg.IsNotNull(options, nameof(options));

        _logger.LogInformation(
            "Processing batch bundle with streaming responses (type: {Type})",
            options.Type);

        // Create coordinator for batch write optimization
        var coordinator = await DeferredWriteCoordinator.CreateAsync(
            channelCapacity: options.ChannelCapacity,
            repositoryFactory: _repositoryFactory,
            partitionStrategy: _partitionStrategy,
            contextAccessor: _contextAccessor,
            logger: _loggerFactory.CreateLogger<DeferredWriteCoordinator>(),
            cancellationToken: cancellationToken);

        _logger.LogDebug("Created DeferredWriteCoordinator for streaming batch bundle");

        // Start background batch processor task
        var batchProcessorTask = System.Threading.Tasks.Task.Run(async () =>
        {
            const int batchSize = 50;
            var allErrors = new List<Exception>();

            _logger.LogDebug("Background batch processor started");

            try
            {
                while (coordinator.PendingOperationCount > 0 ||
                       await WaitForWritesAsync(coordinator, cancellationToken))
                {
                    // Only process batch if there are actually operations queued
                    if (coordinator.PendingOperationCount > 0)
                    {
                        var errors = await coordinator.ProcessBatchAsync(batchSize, cancellationToken);
                        allErrors.AddRange(errors);

                        if (errors.Count > 0)
                        {
                            _logger.LogWarning("Batch processor encountered {ErrorCount} errors", errors.Count);
                        }
                    }
                }

                _logger.LogDebug("Background batch processor completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background batch processor failed");
                throw;
            }
        }, cancellationToken);

        // Execute entries and get streaming responses
        var emptyReferenceContext = new ReferenceResolutionContext();
        var responseStream = _channelExecutor.ExecuteStreamingAsync(
            entryStream,
            emptyReferenceContext,
            options,
            cancellationToken,
            coordinator);

        // Return context with response stream and cleanup task
        return new StreamingBundleContext(
            responseStream,
            coordinator,
            batchProcessorTask,
            _logger);
    }
}
