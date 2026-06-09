// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Application.Utilities;

/// <summary>
/// Debounces cache invalidation requests to avoid thrashing when multiple packages
/// are loaded in quick succession (e.g., bulk imports).
///
/// Strategy: When first invalidation is requested, start a timer. If more requests come
/// before timer expires, reset timer. When timer finally expires, execute single clear.
/// </summary>
public sealed class DebounceInvalidationStrategy : IDisposable
{
    private readonly TimeSpan _debounceDelay;
    private readonly ILogger _logger;

    // Per-tenant debounce state: tenantId → (pendingAction, timer)
    private readonly ConcurrentDictionary<int, DebounceState> _debounceStates;
    private bool _disposed;

    /// <summary>
    /// Gets the configured debounce delay.
    /// </summary>
    public TimeSpan DebounceDelay => _debounceDelay;

    /// <summary>
    /// Initializes a new instance of the <see cref="DebounceInvalidationStrategy"/> class.
    /// </summary>
    /// <param name="debounceDelay">Debounce delay (default: 1 second)</param>
    /// <param name="logger">Logger instance</param>
    public DebounceInvalidationStrategy(
        TimeSpan? debounceDelay = null,
        ILogger? logger = null)
    {
        // Default: 1 second debounce
        _debounceDelay = debounceDelay ?? TimeSpan.FromSeconds(1);
        _logger = logger ?? new NullLogger<DebounceInvalidationStrategy>();
        _debounceStates = new ConcurrentDictionary<int, DebounceState>();
    }

    /// <summary>
    /// Enqueues an invalidation request with debounce protection.
    /// Multiple requests within the debounce window are coalesced into one.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="invalidateAction">Action to execute after debounce window expires</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public void RequestInvalidation(
        int tenantId,
        Func<Task> invalidateAction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invalidateAction);

        if (_disposed)
        {
            _logger.LogWarning("Cannot request invalidation after disposal");
            return;
        }

        _debounceStates.AddOrUpdate(
            tenantId,
            _ => CreateNewDebounceState(tenantId, invalidateAction, cancellationToken),
            (_, existing) => ResetDebounceTimer(tenantId, existing, invalidateAction, cancellationToken));

        _logger.LogDebug(
            "Invalidation request for tenant {TenantId} (debounce window: {DebounceMs}ms)",
            tenantId,
            _debounceDelay.TotalMilliseconds);
    }

    private DebounceState CreateNewDebounceState(
        int tenantId,
        Func<Task> invalidateAction,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Starting debounce timer for tenant {TenantId} ({DebounceMs}ms)",
            tenantId,
            _debounceDelay.TotalMilliseconds);

        // Capture the token before handing it to the timer: the callback may fire after
        // another thread has disposed the CTS, and CancellationTokenSource.Token throws
        // ObjectDisposedException inside an async-void timer callback (kills the process).
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = cts.Token;
        var timer = new Timer(
            async _ => await ExecuteInvalidation(tenantId, invalidateAction, token),
            state: null,
            dueTime: _debounceDelay,
            period: Timeout.InfiniteTimeSpan); // One-shot timer

        return new DebounceState
        {
            Timer = timer,
            CancellationTokenSource = cts,
            LastRequestTime = DateTimeOffset.UtcNow,
        };
    }

    private DebounceState ResetDebounceTimer(
        int tenantId,
        DebounceState existing,
        Func<Task> invalidateAction,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Resetting debounce timer for tenant {TenantId} (request coalesced)",
            tenantId);

        // Cancel old timer
#pragma warning disable CA1849 // Synchronous cleanup during reset is acceptable
        existing.Timer?.Dispose();
#pragma warning restore CA1849
        try
        {
            existing.CancellationTokenSource?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // CTS was already disposed by another thread - safe to ignore
        }

        existing.CancellationTokenSource?.Dispose();

        // Create new timer with reset window; capture the token for the same
        // disposed-CTS race described in CreateNewDebounceState.
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = cts.Token;
        var timer = new Timer(
            async _ => await ExecuteInvalidation(tenantId, invalidateAction, token),
            state: null,
            dueTime: _debounceDelay,
            period: Timeout.InfiniteTimeSpan);

        return new DebounceState
        {
            Timer = timer,
            CancellationTokenSource = cts,
            LastRequestTime = DateTimeOffset.UtcNow,
        };
    }

    private async Task ExecuteInvalidation(
        int tenantId,
        Func<Task> invalidateAction,
        CancellationToken cancellationToken)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("Debounced invalidation cancelled for tenant {TenantId} before execution", tenantId);
                return;
            }

            _logger.LogInformation(
                "Executing debounced invalidation for tenant {TenantId}",
                tenantId);

            await invalidateAction();

            _logger.LogInformation(
                "Debounced invalidation completed for tenant {TenantId}",
                tenantId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Debounced invalidation cancelled for tenant {TenantId}", tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error during debounced invalidation for tenant {TenantId}",
                tenantId);
        }
        finally
        {
            // Clean up completed state
            if (_debounceStates.TryRemove(tenantId, out var state))
            {
#pragma warning disable CA1849 // Synchronous cleanup in finally block is acceptable
                state.Timer?.Dispose();
#pragma warning restore CA1849
                state.CancellationTokenSource?.Dispose();
            }
        }
    }

    /// <summary>
    /// Disposes all pending timers and cancellation tokens.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var state in _debounceStates.Values)
        {
#pragma warning disable CA1849 // Timer doesn't have async dispose in all contexts
            state.Timer?.Dispose();
#pragma warning restore CA1849
            try
            {
                state.CancellationTokenSource?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // CTS was already disposed by another thread - safe to ignore
            }

            state.CancellationTokenSource?.Dispose();
        }

        _debounceStates.Clear();

        GC.SuppressFinalize(this);
    }

    private class DebounceState
    {
        public Timer? Timer { get; set; }

        public CancellationTokenSource? CancellationTokenSource { get; set; }

        public DateTimeOffset LastRequestTime { get; set; }
    }
}
