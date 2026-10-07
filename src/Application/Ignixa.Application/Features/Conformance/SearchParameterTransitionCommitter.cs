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
    private const int MaxConcurrencyAttempts = 3;

    public async Task<bool> CommitAsync(long hideEventId, CancellationToken cancellationToken)
    {
        await conformanceState.CatchUpAsync(eventStore, cancellationToken);
        using var activationLock = await conformanceState.AcquireActivationLockAsync(cancellationToken);
        for (var attempt = 0; attempt < MaxConcurrencyAttempts; attempt++)
        {
            await conformanceState.CatchUpWhileActivationLockHeldAsync(eventStore, cancellationToken);
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

            fhirVersionContext.InvalidateSearchParameterCaches();

            if (candidates.Any(candidate => candidate.ActivationEventIds.Count > 0))
            {
                await reindexTrigger.RequestReindexAsync(
                    $"Search parameter transition {hideEventId} committed",
                    cancellationToken);
            }

            return true;
        }

        throw new InvalidOperationException("Search parameter transition commit did not complete.");
    }
}
