using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Application.Features.Search;
using Ignixa.Search.Definition;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Commits the phase-two state transition for the definitions hidden by one event.
/// </summary>
public sealed class SearchParameterTransitionCommitter(
    ISourceEventStore eventStore,
    ConformanceState conformanceState,
    IReindexTrigger reindexTrigger,
    ConformanceRefreshPublisher refreshPublisher,
    ILogger<SearchParameterTransitionCommitter> logger)
{
    private const int MaxConcurrencyAttempts = 3;

    public async Task<bool> CommitAsync(long hideEventId, CancellationToken cancellationToken)
    {
        var committedTransition = false;
        var reindexRequired = false;
        using (await conformanceState.AcquireActivationLockAsync(cancellationToken))
        {
            for (var attempt = 0; attempt < MaxConcurrencyAttempts; attempt++)
            {
                await conformanceState.CatchUpWhileActivationLockHeldAsync(eventStore, cancellationToken);
                var candidates = conformanceState.GetTransitionCandidates(hideEventId);
                if (candidates.Count == 0)
                {
                    break;
                }

                using var staging = conformanceState.CreateStagingCopy();
                var applicable = new List<(SearchParameterTransitionCandidate Candidate, NewSourceEvent Event)>();
                foreach (var candidate in candidates)
                {
                    var proposed = new NewSourceEvent(
                        $"transition:{hideEventId}",
                        nameof(SearchParameterTransitionCommitted),
                        new SearchParameterTransitionCommitted(
                            candidate.SearchParamId,
                            candidate.ActivationEventIds,
                            candidate.DeactivationEventIds));
                    _ = staging.ApplyProposedEvent(proposed);
                    if (staging.GetTransitionCandidates(hideEventId)
                        .All(current => current.SearchParamId != candidate.SearchParamId))
                    {
                        applicable.Add((candidate, proposed));
                    }
                }

                if (applicable.Count == 0)
                {
                    break;
                }

                var expectedLastEventId = conformanceState.LastProcessedEventId;
                IReadOnlyList<SourceEvent> committed;
                try
                {
                    committed = await eventStore.AppendAsync(
                        applicable.Select(item => item.Event),
                        expectedLastEventId,
                        cancellationToken);
                }
                catch (SourceEventConcurrencyException) when (attempt < MaxConcurrencyAttempts - 1)
                {
                    // A different writer advanced the event stream. Catch up while holding the
                    // activation lock, then commit the still-current candidates on the next attempt.
                    continue;
                }

                foreach (var evt in committed)
                {
                    conformanceState.ApplyAndTrack(evt);
                }

                committedTransition = committed.Count > 0;
                reindexRequired = committedTransition &&
                    applicable.Any(item => item.Candidate.ActivationEventIds.Count > 0);
                break;
            }
        }

        if (reindexRequired)
        {
            try
            {
                _ = await reindexTrigger.RequestReindexAsync(
                    $"Search parameter transition {hideEventId} committed",
                    cancellationToken);
            }
            catch (ReindexTriggerUnavailableException exception)
            {
                ReindexMetrics.RecordTriggerFailure("TransitionCommit");
                logger.LogError(
                    exception,
                    "Search parameter transition {HideEventId} committed durably, but the automatic reindex trigger failed; periodic reconciliation will retry",
                    hideEventId);
            }
        }

        await refreshPublisher.RefreshUntilCurrentAsync(cancellationToken);

        return committedTransition;
    }
}
