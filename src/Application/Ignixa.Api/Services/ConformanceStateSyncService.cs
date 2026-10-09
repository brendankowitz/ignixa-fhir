// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Conformance;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Conformance.Events.Abstractions;
using Microsoft.Extensions.Options;

namespace Ignixa.Api.Services;

/// <summary>
/// Background service that periodically syncs ConformanceState with the event store
/// to enable multi-instance (webfarm) consistency. Polls for new events every N seconds.
/// </summary>
public class ConformanceStateSyncService(
    ISourceEventStore eventStore,
    ConformanceState conformanceState,
    ConformanceRefresher conformanceRefresher,
    ConformanceLease conformanceLease,
    IOptions<ConformanceTransitionOptions> transitionOptions,
    ReindexTrigger reindexTrigger,
    ILogger<ConformanceStateSyncService> logger) : BackgroundService
{
    private long _lastRefreshedEventId;

    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(transitionOptions.Value.SyncIntervalSeconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for initial state to be initialized before starting sync
        while (!conformanceState.IsInitialized && !stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("Waiting for ConformanceState to initialize before starting sync...");
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        logger.LogInformation(
            "ConformanceStateSyncService started. Polling every {Interval}s for new events",
            _pollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_pollInterval, stoppingToken);
                await SyncAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                conformanceLease.Observe();
                logger.LogWarning(
                    ex,
                    "Conformance sync failed at applied EventId {AppliedEventId}, refreshed EventId {RefreshedEventId}; will retry",
                    conformanceState.LastProcessedEventId,
                    _lastRefreshedEventId);
            }
        }

        logger.LogInformation("ConformanceStateSyncService stopped");
    }

    protected async Task SyncAsync(CancellationToken cancellationToken)
    {
        conformanceLease.Observe();
        var syncStart = conformanceLease.CaptureStart();
        var beforeEventId = conformanceState.LastProcessedEventId;
        long afterEventId;

        using (await conformanceState.AcquireActivationLockAsync(cancellationToken))
        {
            await conformanceState.CatchUpWhileActivationLockHeldAsync(eventStore, cancellationToken);
            afterEventId = conformanceState.LastProcessedEventId;
        }

        if (afterEventId > _lastRefreshedEventId || conformanceRefresher.HasPendingRefresh)
        {
            try
            {
                _lastRefreshedEventId = await conformanceRefresher.RefreshAsync(force: false, cancellationToken);
            }
            catch (ConformanceConsumerRefreshException)
            {
                ConformanceMetrics.RecordConsumerRefreshFailure("sync");
                throw;
            }

            // Applying events and refreshing their consumers are separate checkpoints. In particular,
            // an empty subsequent poll must retry a failed refresh of an already-applied event.
            logger.LogInformation(
                "Refreshed conformance consumers through EventId {EventId}",
                _lastRefreshedEventId);
        }

        conformanceLease.Renew(syncStart);

        try
        {
            await reindexTrigger.ReconcileAsync(cancellationToken);
        }
        catch (ReindexTriggerUnavailableException exception)
        {
            ReindexMetrics.RecordReconciliationFailure();
            logger.LogError(
                exception,
                "Reindex reconciliation failed operationally; the next conformance sync will retry");
        }

        if (afterEventId > beforeEventId)
        {
            var eventsApplied = afterEventId - beforeEventId;
            logger.LogInformation(
                "ConformanceStateSyncService caught up: applied {EventCount} events ({Before} -> {After})",
                eventsApplied,
                beforeEventId,
                afterEventId);
        }
        else
        {
            logger.LogDebug("ConformanceStateSyncService: no new events (at EventId {EventId})", afterEventId);
        }
    }
}
