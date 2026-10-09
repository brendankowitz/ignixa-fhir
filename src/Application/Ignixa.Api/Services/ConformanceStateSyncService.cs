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
    ConformanceRefreshPublisher refreshPublisher,
    IConformanceLease conformanceLease,
    ISearchParameterTransitionScheduler transitionScheduler,
    IOptions<ConformanceTransitionOptions> transitionOptions,
    ReindexStartupReconciler reindexReconciler,
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
        long afterEventId;
        IReadOnlyList<long> overdueTransitionIds;

        using (await conformanceState.AcquireActivationLockAsync(cancellationToken))
        {
            await conformanceState.CatchUpWhileActivationLockHeldAsync(eventStore, cancellationToken);
            afterEventId = conformanceState.LastProcessedEventId;
            overdueTransitionIds = GetOverdueTransitionIds();
        }

        if (afterEventId > _lastRefreshedEventId || refreshPublisher.HasPendingRefresh)
        {
            try
            {
                _lastRefreshedEventId = await refreshPublisher.RefreshUntilCurrentAsync(cancellationToken);
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

        await ScheduleOverdueTransitionsAsync(overdueTransitionIds, cancellationToken);
        try
        {
            await reindexReconciler.ReconcileAsync(cancellationToken);
        }
        catch (ReindexTriggerUnavailableException exception)
            when (ReindexTriggerUnavailableException.IsOperational(exception))
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

    private IReadOnlyList<long> GetOverdueTransitionIds()
    {
        var uncommittedTransitionIds = conformanceState.GetTransitionHideEventIds();
        _uncommittedTransitionFirstObserved.Keys
            .Except(uncommittedTransitionIds)
            .ToList()
            .ForEach(eventId => _uncommittedTransitionFirstObserved.Remove(eventId));

        var overdue = new List<long>();
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

            overdue.Add(eventId);
            _uncommittedTransitionFirstObserved[eventId] = _timeProvider.GetTimestamp();
        }

        return overdue;
    }

    private async Task ScheduleOverdueTransitionsAsync(
        IReadOnlyList<long> overdueTransitionIds,
        CancellationToken cancellationToken)
    {
        foreach (var eventId in overdueTransitionIds)
        {
            try
            {
                await transitionScheduler.ScheduleReconciliationAsync(
                    eventId,
                    _transitionGrace,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                _uncommittedTransitionFirstObserved[eventId] = _timeProvider.GetTimestamp();
                ConformanceMetrics.RecordTransitionScheduleFailure();
                logger.LogError(
                    exception,
                    "Transition watchdog could not schedule hide EventId {EventId}; it will retry on the next sync",
                    eventId);
            }
        }
    }
}
