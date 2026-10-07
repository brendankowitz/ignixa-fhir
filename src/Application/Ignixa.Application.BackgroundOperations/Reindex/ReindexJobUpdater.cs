using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed class ReindexJobUpdater(
    IBackgroundJobRepository<ReindexJobDefinition> repository,
    IReindexJobLock jobLock,
    IReindexCompletionHook completionHook)
{
    private const int GlobalTenantId = 1;

    public Task UpdateAsync(
        string jobId,
        Action<BackgroundJob<ReindexJobDefinition>> update,
        CancellationToken cancellationToken) =>
        jobLock.ExecuteAsync(
            async ct =>
            {
                var job = await repository.GetAsync(jobId, GlobalTenantId, ct)
                    ?? throw new InvalidOperationException($"Reindex job {jobId} does not exist.");
                update(job);
                job.HeartbeatDate = DateTimeOffset.UtcNow;
                await repository.UpdateAsync(job, GlobalTenantId, ct);
                return true;
            },
            cancellationToken);

    public Task CompleteAsync(
        string jobId,
        Action<BackgroundJob<ReindexJobDefinition>> update,
        CancellationToken cancellationToken) =>
        jobLock.ExecuteAsync(
            async ct =>
            {
                var job = await repository.GetAsync(jobId, GlobalTenantId, ct)
                    ?? throw new InvalidOperationException($"Reindex job {jobId} does not exist.");
                update(job);
                job.HeartbeatDate = DateTimeOffset.UtcNow;
                try
                {
                    await repository.UpdateAsync(job, GlobalTenantId, ct);
                }
                catch (BackgroundJobUpdateConflictException)
                {
                    job = await repository.GetAsync(jobId, GlobalTenantId, ct)
                        ?? throw new InvalidOperationException($"Reindex job {jobId} disappeared after a terminal update conflict.");
                }

                await completionHook.OnCompletedAsync(job, ct);
                return true;
            },
            cancellationToken);
}
