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
    ConformanceRefresher conformanceRefresher,
    ILogger<SearchParameterTransitionCommitter> logger)
{
    public async Task<bool> CommitAsync(long hideEventId, CancellationToken cancellationToken)
    {
        IReadOnlyList<SourceEvent> committed;
        var reindexRequired = false;
        using (await conformanceState.AcquireActivationLockAsync(cancellationToken))
        {
            committed = await conformanceState.AppendWhileActivationLockHeldAsync(
                eventStore,
                () =>
                {
                    var applicable = GetApplicableCandidates(hideEventId);
                    reindexRequired = applicable.Any(item => item.Candidate.ActivationEventIds.Count > 0);
                    return applicable.Select(item => item.Event).ToArray();
                },
                cancellationToken);
        }

        var committedTransition = committed.Count > 0;
        if (committedTransition && reindexRequired)
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

        await conformanceRefresher.RefreshAsync(force: false, cancellationToken);

        return committedTransition;
    }

    // A candidate is applicable only when its event would change the projection; appending one the
    // state guards ignore would only add a stale event.
    private IReadOnlyList<(SearchParameterTransitionCandidate Candidate, NewSourceEvent Event)> GetApplicableCandidates(
        long hideEventId)
    {
        var candidates = conformanceState.GetTransitionCandidates(hideEventId);
        if (candidates.Count == 0)
        {
            return [];
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

        return applicable;
    }
}
