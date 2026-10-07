using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Builds conformance consumers off-lock and publishes them only while their source generation is current.
/// </summary>
public sealed class ConformanceRefreshPublisher(
    ConformanceState conformanceState,
    IConformanceCacheRefresher cacheRefresher,
    ILogger<ConformanceRefreshPublisher> logger) : IDisposable
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private long _publishedGeneration = -1;

    public async Task<long> RefreshUntilCurrentAsync(CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            while (true)
            {
                ConformanceStateSnapshot stateSnapshot;
                long generation;
                using (await conformanceState.AcquireActivationLockAsync(cancellationToken))
                {
                    generation = conformanceState.LastProcessedEventId;
                    if (generation <= Interlocked.Read(ref _publishedGeneration))
                    {
                        return generation;
                    }

                    stateSnapshot = conformanceState.CreateSnapshot();
                }

                var consumerSnapshot = await cacheRefresher.BuildSnapshotAsync(
                    stateSnapshot,
                    generation,
                    cancellationToken);

                using (await conformanceState.AcquireActivationLockAsync(cancellationToken))
                {
                    if (conformanceState.LastProcessedEventId != generation)
                    {
                        logger.LogInformation(
                            "Discarding conformance consumer snapshot for EventId {SnapshotEventId}; projection advanced to EventId {CurrentEventId}",
                            generation,
                            conformanceState.LastProcessedEventId);
                        continue;
                    }

                    cacheRefresher.PublishSnapshot(consumerSnapshot);
                    Interlocked.Exchange(ref _publishedGeneration, generation);
                    return generation;
                }
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Dispose()
    {
        _refreshLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
