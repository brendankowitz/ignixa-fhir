// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events.Abstractions;

namespace Ignixa.Api.Services;

/// <summary>
/// Background service that periodically syncs ConformanceState with the event store
/// to enable multi-instance (webfarm) consistency. Polls for new events every N seconds.
/// </summary>
public class ConformanceStateSyncService(
    ISourceEventStore eventStore,
    ConformanceState conformanceState,
    ILogger<ConformanceStateSyncService> logger,
    IConfiguration configuration) : BackgroundService
{
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(
        configuration.GetValue("Conformance:SyncIntervalSeconds", 30));

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
                logger.LogWarning(ex, "ConformanceStateSyncService encountered an error during sync, will retry");
            }
        }

        logger.LogInformation("ConformanceStateSyncService stopped");
    }

    private async Task SyncAsync(CancellationToken cancellationToken)
    {
        var beforeEventId = conformanceState.LastProcessedEventId;

        await conformanceState.CatchUpAsync(eventStore, cancellationToken);

        var afterEventId = conformanceState.LastProcessedEventId;

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
