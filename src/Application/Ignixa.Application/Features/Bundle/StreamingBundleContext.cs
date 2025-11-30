// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Bundle;

/// <summary>
/// Context for streaming bundle processing.
/// Contains the response stream and manages cleanup of background tasks.
/// </summary>
public class StreamingBundleContext : IAsyncDisposable
{
    private readonly DeferredWriteCoordinator _coordinator;
    private readonly Task _batchProcessorTask;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamingBundleContext"/> class.
    /// </summary>
    public StreamingBundleContext(
        IAsyncEnumerable<BundleEntryResponse> responseStream,
        DeferredWriteCoordinator coordinator,
        Task batchProcessorTask,
        ILogger logger)
    {
        ResponseStream = responseStream ?? throw new ArgumentNullException(nameof(responseStream));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _batchProcessorTask = batchProcessorTask ?? throw new ArgumentNullException(nameof(batchProcessorTask));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the stream of bundle entry responses.
    /// </summary>
    public IAsyncEnumerable<BundleEntryResponse> ResponseStream { get; }

    /// <summary>
    /// Completes the streaming bundle processing and waits for background tasks to finish.
    /// </summary>
    public async Task CompleteAsync()
    {
        _logger.LogDebug("Completing streaming bundle context");

        // Signal coordinator to complete
        _coordinator.CompleteWrites();

        // Wait for batch processor to finish
        _logger.LogDebug("Waiting for batch processor to complete");
        await _batchProcessorTask;

        _logger.LogInformation("Streaming bundle context completed successfully");
    }

    /// <summary>
    /// Disposes the streaming bundle context and ensures cleanup.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await CompleteAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during streaming bundle context disposal");
            throw;
        }

        GC.SuppressFinalize(this);
    }
}
