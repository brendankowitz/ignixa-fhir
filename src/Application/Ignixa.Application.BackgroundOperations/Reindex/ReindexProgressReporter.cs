using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.Reindex;

/// <summary>
/// Writes a job's heartbeat and reported progress without the singleton job lock. A job that is already
/// finished or gone, or that finishes between the read and the write, is left untouched and reported as
/// closed so the orchestration can stop.
/// </summary>
public sealed class ReindexProgressReporter(
    IBackgroundJobRepository<ReindexJobDefinition> repository,
    TimeProvider timeProvider,
    ILogger<ReindexProgressReporter> logger)
{
    /// <summary>Refreshes <c>HeartbeatDate</c>, which is what stale-job detection reads.</summary>
    public Task<bool> HeartbeatAsync(string jobId, CancellationToken cancellationToken) =>
        UpdateAsync(jobId, _ => { }, cancellationToken);

    /// <summary>Replaces the job's reported progress with the orchestration's current snapshot.</summary>
    public Task<bool> ReportAsync(string jobId, ReindexProgress progress, CancellationToken cancellationToken) =>
        UpdateAsync(jobId, job => job.Progress = progress.ToJson(), cancellationToken);

    /// <summary>
    /// Runs one activity's work between two heartbeats. The job store must be reachable to start; a heartbeat
    /// that fails after the work is done is logged and metered rather than throwing the finished work away.
    /// </summary>
    public async Task<T> RunWithHeartbeatAsync<T>(
        string jobId,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken)
    {
        await HeartbeatAsync(jobId, cancellationToken);
        var result = await work(cancellationToken);
        try
        {
            await HeartbeatAsync(jobId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ReindexMetrics.ProgressPersistenceFailed();
            logger.LogWarning(ex,
                "Reindex job {JobId} heartbeat failed after an activity finished; the next activity or snapshot refreshes it",
                jobId);
        }

        return result;
    }

    private async Task<bool> UpdateAsync(
        string jobId,
        Action<BackgroundJob<ReindexJobDefinition>> update,
        CancellationToken cancellationToken)
    {
        var job = await repository.GetAsync(jobId, SystemConstants.GlobalTenantId, cancellationToken);
        if (job is null || job.IsTerminal())
        {
            return false;
        }

        update(job);
        job.HeartbeatDate = timeProvider.GetUtcNow();
        try
        {
            await repository.UpdateAsync(job, SystemConstants.GlobalTenantId, cancellationToken);
            return true;
        }
        catch (BackgroundJobUpdateConflictException)
        {
            // The job closed between the read and the write; the terminal state is authoritative.
            return false;
        }
    }
}
