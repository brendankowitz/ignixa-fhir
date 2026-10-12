using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex;

/// <summary>
/// Writes a job's heartbeat and reported progress without the singleton job lock. A job that is already
/// finished or gone, or that finishes between the read and the write, is left untouched and reported as
/// closed so the orchestration can stop.
/// </summary>
public sealed class ReindexProgressReporter(
    IBackgroundJobRepository<ReindexJobDefinition> repository,
    TimeProvider timeProvider)
{
    /// <summary>Refreshes <c>HeartbeatDate</c>, which is what stale-job detection reads.</summary>
    public Task<bool> HeartbeatAsync(string jobId, CancellationToken cancellationToken) =>
        UpdateAsync(jobId, _ => { }, cancellationToken);

    /// <summary>Replaces the job's reported progress with the orchestration's current snapshot.</summary>
    public Task<bool> ReportAsync(string jobId, ReindexProgress progress, CancellationToken cancellationToken) =>
        UpdateAsync(jobId, job => job.Progress = progress.ToJson(), cancellationToken);

    /// <summary>
    /// Refreshes the heartbeat, then runs one activity's work. The job store must be reachable to start; the
    /// next activity or progress snapshot refreshes the heartbeat after the work.
    /// </summary>
    public async Task<T> RunWithHeartbeatAsync<T>(
        string jobId,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken)
    {
        await HeartbeatAsync(jobId, cancellationToken);
        return await work(cancellationToken);
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
