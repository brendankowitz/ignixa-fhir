// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using EnsureThat;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Bundle;

/// <summary>
/// Executes bundle entries in parallel using System.Threading.Channels for bounded parallelism.
/// Groups entries by HTTP verb to enforce FHIR execution order (DELETE → POST → PUT → PATCH → GET).
/// </summary>
public class BundleChannelExecutor
{
    private readonly BundleEntryExecutor _entryExecutor;
    private readonly ILogger<BundleChannelExecutor> _logger;

    /// <summary>
    /// FHIR transaction verb order priority (lower number = must execute first).
    /// Defines the execution order: DELETE → POST → PUT → PATCH → GET
    /// Per FHIR spec: https://build.fhir.org/http.html#trules
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> VerbOrderPriority = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["DELETE"] = 1,
        ["POST"] = 2,
        ["PUT"] = 3,
        ["PATCH"] = 4,
        ["GET"] = 5
    };

    public BundleChannelExecutor(
        BundleEntryExecutor entryExecutor,
        ILogger<BundleChannelExecutor> logger)
    {
        _entryExecutor = EnsureArg.IsNotNull(entryExecutor, nameof(entryExecutor));
        _logger = EnsureArg.IsNotNull(logger, nameof(logger));
    }

    /// <summary>
    /// Gets the FHIR transaction verb priority for a given HTTP verb.
    /// Returns int.MaxValue for unknown verbs.
    /// </summary>
    public static int GetVerbPriority(string httpVerb)
    {
        return VerbOrderPriority.TryGetValue(httpVerb, out int priority) ? priority : int.MaxValue;
    }

    /// <summary>
    /// Executes bundle entries in streaming mode - processes entries as they arrive and yields responses in order.
    /// Happy path for batch bundles and streaming transaction bundles.
    /// For batch: executes entries in parallel as they arrive.
    /// For transaction with streaming: validates verb order and executes sequentially.
    /// </summary>
    /// <param name="entryStream">Stream of bundle entries to execute.</param>
    /// <param name="referenceContext">Reference resolution context (empty for streaming mode).</param>
    /// <param name="options">Processing options (parallelism, channel capacity, bundle type).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="deferredWriteCoordinator">Optional coordinator for deferred batch writes.</param>
    /// <param name="skipStreamingValidation">
    /// If true, skips streaming entry validation (e.g., urn:uuid, conditional reference checks).
    /// Use this for Phase 2 buffered entries that have already been validated during Phase 1.
    /// Skipping validation prevents re-validation of conditional references that were resolved in Phase 1.
    /// Default: false (perform validation, which is correct for Phase 1 streaming entries).
    /// </param>
    /// <returns>Async enumerable of responses in the same order as input entries.</returns>
    public async IAsyncEnumerable<BundleEntryResponse> ExecuteStreamingAsync(
        IAsyncEnumerable<BundleEntryContext> entryStream,
        ReferenceResolutionContext referenceContext,
        BundleProcessingOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        DeferredWriteCoordinator? deferredWriteCoordinator = null,
        bool skipStreamingValidation = false)
    {
        EnsureArg.IsNotNull(entryStream, nameof(entryStream));
        EnsureArg.IsNotNull(referenceContext, nameof(referenceContext));
        EnsureArg.IsNotNull(options, nameof(options));

        _logger.LogInformation(
            "Executing bundle entries with streaming responses (type: {Type}, parallelism: {Parallelism}, skipValidation: {SkipValidation})",
            options.Type,
            options.MaxParallelism,
            skipStreamingValidation);

        // For transaction bundles with streaming, validate verb order and execute sequentially
        if (options.Type == BundleType.Transaction)
        {
            await foreach (var response in ExecuteTransactionStreamingAsync(
                entryStream,
                referenceContext,
                cancellationToken,
                deferredWriteCoordinator,
                skipStreamingValidation))
            {
                yield return response;
            }
            yield break;
        }

        // For batch bundles, execute in parallel and yield responses in order
        await foreach (var response in ExecuteBatchStreamingAsync(
            entryStream,
            referenceContext,
            options,
            cancellationToken,
            deferredWriteCoordinator,
            skipStreamingValidation))
        {
            yield return response;
        }
    }

    /// <summary>
    /// Executes batch bundle entries in streaming mode with parallel execution.
    /// No verb ordering - processes all entries in parallel as they arrive.
    /// </summary>
    /// <param name="entryStream">Stream of bundle entries to execute.</param>
    /// <param name="referenceContext">Reference resolution context.</param>
    /// <param name="options">Processing options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="deferredWriteCoordinator">Optional coordinator for deferred batch writes.</param>
    /// <param name="skipStreamingValidation">
    /// If true, skips streaming entry validation (e.g., urn:uuid, conditional reference checks).
    /// Use this for Phase 2 buffered entries that have already been validated during Phase 1.
    /// Skipping validation prevents re-validation of conditional references that were resolved in Phase 1.
    /// Default: false (perform validation, which is correct for Phase 1 streaming entries).
    /// </param>
    private async IAsyncEnumerable<BundleEntryResponse> ExecuteBatchStreamingAsync(
        IAsyncEnumerable<BundleEntryContext> entryStream,
        ReferenceResolutionContext referenceContext,
        BundleProcessingOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        DeferredWriteCoordinator? deferredWriteCoordinator = null,
        bool skipStreamingValidation = false)
    {
        _logger.LogInformation("Executing batch bundle in streaming mode with parallel execution (skipValidation: {SkipValidation})", skipStreamingValidation);

        // Create response channel with (index, response) tuples
        var responseChannel = Channel.CreateBounded<(int index, BundleEntryResponse response)>(
            new BoundedChannelOptions(options.ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

        // Execute entries in parallel and write to response channel
        var executionTask = Task.Run(async () =>
        {
            try
            {
                // Create entry channel for work distribution
                var entryChannel = Channel.CreateBounded<(int index, BundleEntryContext entry)>(
                    new BoundedChannelOptions(options.ChannelCapacity)
                    {
                        FullMode = BoundedChannelFullMode.Wait,
                        SingleReader = false,
                        SingleWriter = true
                    });

                // Producer: Feed entries from stream to channel as they arrive
                var producerTask = Task.Run(async () =>
                {
                    try
                    {
                        await foreach (var entry in entryStream.WithCancellation(cancellationToken))
                        {
                            // Validate no urn:uuid or conditional references in streaming mode
                            // Skip validation if entries were pre-processed (buffered Phase 2)
                            if (!skipStreamingValidation)
                            {
                                ValidateStreamingEntry(entry);
                            }

                            await entryChannel.Writer.WriteAsync((entry.Index, entry), cancellationToken);
                        }
                    }
                    finally
                    {
                        entryChannel.Writer.Complete();
                    }
                }, cancellationToken);

                // Consumers: Process entries in parallel and write responses
                var consumerTasks = Enumerable.Range(0, options.MaxParallelism)
                    .Select(_ => Task.Run(async () =>
                    {
                        await foreach (var (index, entry) in entryChannel.Reader.ReadAllAsync(cancellationToken))
                        {
                            var response = await _entryExecutor.ExecuteAsync(
                                entry,
                                referenceContext,
                                cancellationToken,
                                deferredWriteCoordinator);

                            await responseChannel.Writer.WriteAsync((index, response), cancellationToken);
                        }
                    }, cancellationToken))
                    .ToArray();

                await producerTask;
                await Task.WhenAll(consumerTasks);
            }
            finally
            {
                responseChannel.Writer.Complete();
            }
        }, cancellationToken);

        // Yield responses in order
        var completedResponses = new Dictionary<int, BundleEntryResponse>();
        int nextIndex = 0;

        await foreach (var (index, response) in responseChannel.Reader.ReadAllAsync(cancellationToken))
        {
            completedResponses[index] = response;

            // Yield all consecutive responses starting from nextIndex
            while (completedResponses.TryGetValue(nextIndex, out var nextResponse))
            {
                _logger.LogTrace("Yielding batch response for entry {Index}", nextIndex);
                yield return nextResponse;
                completedResponses.Remove(nextIndex);
                nextIndex++;
            }
        }

        // Wait for execution to complete
        await executionTask;

        _logger.LogInformation("Streaming batch execution complete");
    }

    /// <summary>
    /// Executes transaction bundle entries in streaming mode with verb-grouped parallel execution.
    /// Streams entries into channel as they arrive, drains channel when verb changes.
    /// Validates DELETE → POST → PUT → PATCH → GET order.
    /// </summary>
    /// <param name="entryStream">Stream of bundle entries to execute.</param>
    /// <param name="referenceContext">Reference resolution context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="deferredWriteCoordinator">Optional coordinator for deferred batch writes.</param>
    /// <param name="skipStreamingValidation">Skip validation for urn:uuid/conditional refs (set true when entries were pre-processed/buffered).</param>
    private async IAsyncEnumerable<BundleEntryResponse> ExecuteTransactionStreamingAsync(
        IAsyncEnumerable<BundleEntryContext> entryStream,
        ReferenceResolutionContext referenceContext,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        DeferredWriteCoordinator? deferredWriteCoordinator = null,
        bool skipStreamingValidation = false)
    {
        _logger.LogInformation("Executing transaction bundle in streaming mode with verb-grouped parallel execution (skipValidation: {SkipValidation})", skipStreamingValidation);

        // Create channels for work distribution
        var entryChannel = Channel.CreateUnbounded<(int index, BundleEntryContext entry)>(
            new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = true
            });

        var responseChannel = Channel.CreateUnbounded<(int index, BundleEntryResponse response)>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

        // Start consumer tasks that will process entries as they arrive
        var consumerTasks = Enumerable.Range(0, Math.Max(1, Environment.ProcessorCount))
            .Select(_ => Task.Run(async () =>
            {
                await foreach (var (index, entry) in entryChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    var response = await _entryExecutor.ExecuteAsync(
                        entry,
                        referenceContext,
                        cancellationToken,
                        deferredWriteCoordinator);

                    await responseChannel.Writer.WriteAsync((index, response), cancellationToken);

                    _logger.LogTrace("Completed transaction entry {Index}: {Verb} {Url}",
                        entry.Index, entry.HttpVerb, entry.RequestUrl);
                }
            }, cancellationToken))
            .ToArray();

        // Producer task: feed entries to channel, draining when verb changes
        var producerTask = Task.Run(async () =>
        {
            try
            {
                int lastVerbPriority = 0;
                int totalEntries = 0;
                int currentVerbEntries = 0;
                string? currentVerb = null;

                await foreach (var entry in entryStream.WithCancellation(cancellationToken))
                {
                    totalEntries++;

                    // Validate no urn:uuid or conditional references
                    // Skip validation if entries were pre-processed (buffered Phase 2)
                    if (!skipStreamingValidation)
                    {
                        ValidateStreamingEntry(entry);
                    }

                    // Validate verb order
                    if (!VerbOrderPriority.TryGetValue(entry.HttpVerb, out int currentVerbPriority))
                    {
                        throw new InvalidOperationException(
                            $"Entry {entry.Index}: Unknown HTTP verb '{entry.HttpVerb}' in streaming transaction bundle. " +
                            "Supported verbs: DELETE, POST, PUT, PATCH, GET");
                    }

                    if (currentVerbPriority < lastVerbPriority)
                    {
                        throw new InvalidOperationException(
                            $"Entry {entry.Index}: Verb order violation in streaming transaction bundle. " +
                            $"Expected verbs to be ordered as DELETE → POST → PUT → PATCH → GET, " +
                            $"but got {entry.HttpVerb} after a verb with higher priority. " +
                            "Streaming transaction bundles require entries to be pre-sorted by verb.");
                    }

                    // Check if verb changed - if so, drain channel before continuing
                    if (currentVerb != null && !entry.HttpVerb.Equals(currentVerb, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug(
                            "Verb changed from {OldVerb} to {NewVerb}, waiting for {Count} entries to complete",
                            currentVerb,
                            entry.HttpVerb,
                            currentVerbEntries);

                        // Signal verb boundary - complete the channel temporarily
                        // This will cause consumers to finish processing current verb's entries
                        entryChannel.Writer.Complete();

                        // Wait for all consumer tasks to finish processing
                        await Task.WhenAll(consumerTasks);

                        _logger.LogDebug(
                            "All {Count} {Verb} entries completed, starting {NewVerb} verb group",
                            currentVerbEntries,
                            currentVerb,
                            entry.HttpVerb);

                        // Recreate channel for next verb group
                        entryChannel = Channel.CreateUnbounded<(int index, BundleEntryContext entry)>(
                            new UnboundedChannelOptions
                            {
                                SingleReader = false,
                                SingleWriter = true
                            });

                        // Restart consumer tasks for next verb
                        consumerTasks = Enumerable.Range(0, Math.Max(1, Environment.ProcessorCount))
                            .Select(_ => Task.Run(async () =>
                            {
                                await foreach (var (index, e) in entryChannel.Reader.ReadAllAsync(cancellationToken))
                                {
                                    var response = await _entryExecutor.ExecuteAsync(
                                        e,
                                        referenceContext,
                                        cancellationToken,
                                        deferredWriteCoordinator);

                                    await responseChannel.Writer.WriteAsync((index, response), cancellationToken);

                                    _logger.LogTrace("Completed transaction entry {Index}: {Verb} {Url}",
                                        e.Index, e.HttpVerb, e.RequestUrl);
                                }
                            }, cancellationToken))
                            .ToArray();

                        currentVerbEntries = 0;
                    }

                    // Update tracking
                    currentVerb = entry.HttpVerb;
                    lastVerbPriority = currentVerbPriority;
                    currentVerbEntries++;

                    // Write entry to channel for parallel processing
                    await entryChannel.Writer.WriteAsync((entry.Index, entry), cancellationToken);
                }

                // Complete final verb group
                entryChannel.Writer.Complete();
                await Task.WhenAll(consumerTasks);

                _logger.LogInformation(
                    "Streaming transaction execution complete: {Count} entries processed",
                    totalEntries);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in transaction producer task");
                entryChannel.Writer.Complete(ex);
                throw;
            }
            finally
            {
                responseChannel.Writer.Complete();
            }
        }, cancellationToken);

        // Yield responses in order as they become available
        var completedResponses = new Dictionary<int, BundleEntryResponse>();
        int nextIndex = 0;

        await foreach (var (index, response) in responseChannel.Reader.ReadAllAsync(cancellationToken))
        {
            completedResponses[index] = response;

            // Yield all consecutive responses starting from nextIndex
            while (completedResponses.TryGetValue(nextIndex, out var nextResponse))
            {
                _logger.LogTrace("Yielding transaction response for entry {Index}", nextIndex);
                yield return nextResponse;
                completedResponses.Remove(nextIndex);
                nextIndex++;
            }
        }

        // Wait for producer to complete
        await producerTask;
    }

    /// <summary>
    /// Validates that an entry is suitable for streaming mode (no urn:uuid or conditional references).
    /// </summary>
    private void ValidateStreamingEntry(BundleEntryContext entry)
    {
        // Check for urn:uuid in fullUrl
        if (entry.FullUrl?.StartsWith("urn:uuid:", StringComparison.Ordinal) == true)
        {
            throw new InvalidOperationException(
                $"Entry {entry.Index}: Streaming mode does not support urn:uuid references. " +
                $"Found fullUrl='{entry.FullUrl}'. Use fully-resolved resource IDs or switch to buffered mode.");
        }

        // Check for urn:uuid in request.url
        if (entry.RequestUrl?.StartsWith("urn:uuid:", StringComparison.Ordinal) == true)
        {
            throw new InvalidOperationException(
                $"Entry {entry.Index}: Streaming mode does not support urn:uuid references. " +
                $"Found request.url='{entry.RequestUrl}'. Use fully-resolved resource IDs or switch to buffered mode.");
        }

        // Check for conditional references (contains ?)
        if (entry.RequestUrl?.Contains('?', StringComparison.Ordinal) == true)
        {
            throw new InvalidOperationException(
                $"Entry {entry.Index}: Streaming mode does not support conditional references. " +
                $"Found request.url='{entry.RequestUrl}'. Use direct resource IDs (e.g., 'Patient/123') or switch to buffered mode.");
        }
    }
}
