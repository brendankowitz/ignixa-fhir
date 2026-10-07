// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Conformance;
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
    IConformanceCacheRefresher cacheRefresher,
    IConformanceLease conformanceLease,
    ISearchParameterTransitionScheduler transitionScheduler,
    IOptions<ConformanceTransitionOptions> transitionOptions,
    TimeProvider timeProvider,
    ILogger<ConformanceStateSyncService> logger,
    IConfiguration configuration) : BackgroundService
{
    private long _lastRefreshedEventId;
    private readonly Dictionary<long, long> _uncommittedTransitionFirstObserved = [];
    private readonly TimeSpan _transitionGrace = transitionOptions.Value.TransitionGrace;
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

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
                _ = conformanceLease.IsHeld;
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
        _ = conformanceLease.IsHeld;
        var syncStart = conformanceLease.CaptureStart();
        var beforeEventId = conformanceState.LastProcessedEventId;

        await conformanceState.CatchUpAsync(eventStore, cancellationToken);

        // CatchUpAsync takes this same lock. Acquire it only after catch-up has returned, and
        // hold it through refresh so local activation cannot change the definitions being synced.
        using var activationLock = await conformanceState.AcquireActivationLockAsync(cancellationToken);
        var afterEventId = conformanceState.LastProcessedEventId;

        await ObserveUncommittedTransitionsAsync(cancellationToken);

        if (afterEventId > _lastRefreshedEventId)
        {
            try
            {
                await cacheRefresher.RefreshAsync(cancellationToken);
            }
            catch (ConformanceConsumerRefreshException)
            {
                ConformanceConsumerRefreshMetrics.RecordFailure("sync");
                throw;
            }

            // Applying events and refreshing their consumers are separate checkpoints. In particular,
            // an empty subsequent poll must retry a failed refresh of an already-applied event.
            _lastRefreshedEventId = afterEventId;
            logger.LogInformation("Refreshed conformance consumers through EventId {EventId}", afterEventId);
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

        conformanceLease.Renew(syncStart);
    }

    private async Task ObserveUncommittedTransitionsAsync(CancellationToken cancellationToken)
    {
        var uncommittedTransitionIds = conformanceState.GetTransitionHideEventIds();
        _uncommittedTransitionFirstObserved.Keys
            .Except(uncommittedTransitionIds)
            .ToList()
            .ForEach(eventId => _uncommittedTransitionFirstObserved.Remove(eventId));

        foreach (var eventId in uncommittedTransitionIds)
        {
            var now = _timeProvider.GetTimestamp();
            if (!_uncommittedTransitionFirstObserved.TryGetValue(eventId, out var firstObserved))
            {
                _uncommittedTransitionFirstObserved[eventId] = now;
                continue;
            }

            if (_timeProvider.GetElapsedTime(firstObserved) < _transitionGrace + _transitionGrace)
            {
                continue;
            }

            try
            {
                await transitionScheduler.ScheduleReconciliationAsync(
                    eventId,
                    _transitionGrace,
                    cancellationToken);
                _uncommittedTransitionFirstObserved[eventId] = _timeProvider.GetTimestamp();
            }
            catch (Exception exception)
            {
                ConformanceTransitionMetrics.RecordScheduleFailure();
                logger.LogError(
                    exception,
                    "Transition watchdog could not schedule hide EventId {EventId}; it will retry on the next sync",
                    eventId);
            }
        }
    }
}
