namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Refreshes the local consumers of conformance state after replay or activation.
/// </summary>
public interface IConformanceCacheRefresher
{
    Task<IConformanceConsumerSnapshot> BuildSnapshotAsync(
        ConformanceStateSnapshot stateSnapshot,
        long generation,
        CancellationToken cancellationToken);

    void PublishSnapshot(IConformanceConsumerSnapshot snapshot);
}
