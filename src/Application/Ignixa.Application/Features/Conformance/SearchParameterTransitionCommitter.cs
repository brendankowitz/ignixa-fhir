using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Application.Features.Search;
using Ignixa.Search.Definition;

namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Commits the phase-two state transition for every definition one transition hid: all codes of a package
/// activation (keyed by its PackageActivated event id, appended in the same transaction as its hide events),
/// or the definitions of one deactivation event. One append, one refresh and one reindex request per transition.
/// </summary>
public sealed class SearchParameterTransitionCommitter(
    ISourceEventStore eventStore,
    ConformanceState conformanceState,
    IReindexTrigger reindexTrigger,
    ConformanceRefresher conformanceRefresher)
{
    /// <returns>Whether a transition event was appended; false when nothing it hid is still pending.</returns>
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
            // The trigger defers its own operational failures; periodic reconciliation retries them.
            _ = await reindexTrigger.RequestReindexAsync(
                $"Search parameter transition {hideEventId} committed",
                cancellationToken);
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
