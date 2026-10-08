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
    private bool _forcePending;

    public Task<long> RefreshUntilCurrentAsync(CancellationToken cancellationToken) =>
        RefreshAsync(forceRefresh: false, cancellationToken);

    public Task<long> RefreshCurrentAsync(CancellationToken cancellationToken) =>
        RefreshAsync(forceRefresh: true, cancellationToken);

    private async Task<long> RefreshAsync(bool forceRefresh, CancellationToken cancellationToken)
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
                    if (!forceRefresh &&
                        !_forcePending &&
                        generation <= Interlocked.Read(ref _publishedGeneration))
                    {
                        return generation;
                    }

                    stateSnapshot = conformanceState.CreateSnapshot();
                }

                IConformanceConsumerSnapshot consumerSnapshot;
                try
                {
                    consumerSnapshot = await cacheRefresher.BuildSnapshotAsync(
                        stateSnapshot,
                        generation,
                        cancellationToken);
                }
                catch (ConformanceConsumerRefreshException) when (forceRefresh)
                {
                    _forcePending = true;
                    throw;
                }

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
                    _forcePending = false;
                }

                await cacheRefresher.InvalidatePublishedSnapshotCachesAsync(
                    consumerSnapshot,
                    CancellationToken.None);
                return generation;
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
