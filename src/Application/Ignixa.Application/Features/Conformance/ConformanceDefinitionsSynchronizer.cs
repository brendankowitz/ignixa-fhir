using Ignixa.Conformance.Events.Abstractions;

namespace Ignixa.Application.Features.Conformance;

public sealed class ConformanceDefinitionsSynchronizer(
    ISourceEventStore eventStore,
    ConformanceState conformanceState,
    IConformanceCacheRefresher cacheRefresher) : IConformanceDefinitionsSynchronizer
{
    public async Task SynchronizeAsync(CancellationToken cancellationToken)
    {
        await conformanceState.CatchUpAsync(eventStore, cancellationToken);

        using var activationLock = await conformanceState.AcquireActivationLockAsync(cancellationToken);
        await cacheRefresher.RefreshAsync(conformanceState.LastProcessedEventId, cancellationToken);
    }
}
