using Ignixa.Conformance.Events.Abstractions;

namespace Ignixa.Application.Features.Conformance;

public sealed class ConformanceDefinitionsSynchronizer(
    ISourceEventStore eventStore,
    ConformanceState conformanceState,
    ConformanceRefreshPublisher refreshPublisher) : IConformanceDefinitionsSynchronizer
{
    public async Task SynchronizeAsync(CancellationToken cancellationToken)
    {
        using (await conformanceState.AcquireActivationLockAsync(cancellationToken))
        {
            await conformanceState.CatchUpWhileActivationLockHeldAsync(eventStore, cancellationToken);
        }

        await refreshPublisher.RefreshUntilCurrentAsync(cancellationToken);
    }
}
