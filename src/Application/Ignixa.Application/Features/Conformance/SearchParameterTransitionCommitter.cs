using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Application.Features.Search;
using Ignixa.Search.Definition;

namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Commits the phase-two state transition for the definitions hidden by one event.
/// </summary>
public sealed class SearchParameterTransitionCommitter(
    ISourceEventStore eventStore,
    ConformanceState conformanceState,
    IReindexTrigger reindexTrigger,
    IFhirVersionContext fhirVersionContext)
{
    public async Task<bool> CommitAsync(long hideEventId, CancellationToken cancellationToken)
    {
        await conformanceState.CatchUpAsync(eventStore, cancellationToken);
        using var activationLock = await conformanceState.AcquireActivationLockAsync(cancellationToken);
        var candidates = conformanceState.GetTransitionCandidates(hideEventId);
        if (candidates.Count == 0)
        {
            return false;
        }

        var expectedLastEventId = conformanceState.LastProcessedEventId;
        IReadOnlyList<SourceEvent> committed;
        try
        {
            committed = await eventStore.AppendAsync(
                candidates.Select(candidate => new NewSourceEvent(
                    $"transition:{hideEventId}",
                    nameof(SearchParameterTransitionCommitted),
                    new SearchParameterTransitionCommitted(
                        candidate.SearchParamId,
                        candidate.ActivationEventIds,
                        candidate.DeactivationEventIds))),
                expectedLastEventId,
                cancellationToken);
        }
        catch (SourceEventConcurrencyException)
        {
            // A concurrent committer or activation owns the newer projection. Its transition will
            // be scheduled from that durable event, so this invocation is safely obsolete.
            return false;
        }

        foreach (var evt in committed)
        {
            conformanceState.ApplyAndTrack(evt);
        }

        fhirVersionContext.InvalidateSearchParameterCaches();

        if (candidates.Any(candidate => candidate.ActivationEventIds.Count > 0))
        {
            await reindexTrigger.RequestReindexAsync(
                $"Search parameter transition {hideEventId} committed",
                cancellationToken);
        }

        return true;
    }
}
