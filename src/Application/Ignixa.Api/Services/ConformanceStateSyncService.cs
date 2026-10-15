// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Conformance;
using Ignixa.Application.BackgroundOperations.Reindex;
using Microsoft.Extensions.Options;

namespace Ignixa.Api.Services;

/// <summary>
/// Background service that periodically syncs ConformanceState with the event store
/// to enable multi-instance (webfarm) consistency. Polls for new events every N seconds.
/// </summary>
public class ConformanceStateSyncService(
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

        // The lease start precedes the catch-up read, so the lease never claims freshness the read lacked.
        var syncStart = conformanceLease.CaptureStart();
        var beforeEventId = conformanceState.LastProcessedEventId;
        try
        {
            _lastRefreshedEventId = await conformanceRefresher.SynchronizeAsync(cancellationToken);
        }
        catch (ConformanceConsumerRefreshException)
        {
            ConformanceMetrics.RecordConsumerRefreshFailure("sync");
            throw;
        }

        conformanceLease.Renew(syncStart);

        // The trigger defers its own operational failures; a programmer error fails this tick loudly.
        await reindexTrigger.ReconcileAsync(cancellationToken);

        if (_lastRefreshedEventId > beforeEventId)
        {
            logger.LogInformation(
                "ConformanceStateSyncService caught up and refreshed consumers: EventId {Before} -> {After}",
                beforeEventId,
                _lastRefreshedEventId);
        }
        else
        {
            logger.LogDebug("ConformanceStateSyncService: no new events (at EventId {EventId})", _lastRefreshedEventId);
        }
    }
}
