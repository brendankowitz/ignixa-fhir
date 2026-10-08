using Ignixa.Application.Features.Conformance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed class ReindexActivityHeartbeat(
    ReindexProgressReporter progress,
    IOptions<ReindexOptions> options,
    TimeProvider timeProvider,
    ILogger<ReindexActivityHeartbeat> logger)
{
    internal static TimeSpan GetInterval(TimeSpan staleJobTimeout) =>
        TimeSpan.FromTicks(Math.Max(
            TimeSpan.TicksPerMillisecond, Math.Min(TimeSpan.FromSeconds(30).Ticks, staleJobTimeout.Ticks / 4)));

    public async Task<T> RunAsync<T>(
        string jobId,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = HeartbeatAsync(jobId, stop.Token);
        try
        {
            return await work(cancellationToken);
        }
        finally
        {
            await stop.CancelAsync();
            await heartbeat;
        }
    }

    private async Task HeartbeatAsync(string jobId, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(GetInterval(options.Value.StaleJobTimeout), timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    if (!await progress.HeartbeatAsync(jobId, cancellationToken))
                    {
                        return;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    ReindexMetrics.ProgressPersistenceFailed();
                    logger.LogWarning(ex,
                        "Reindex job {JobId} heartbeat persistence failed; retrying on the next heartbeat without failing activity work",
                        jobId);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The owning activity has finished; stopping its heartbeat is not a persistence failure.
        }
    }
}
