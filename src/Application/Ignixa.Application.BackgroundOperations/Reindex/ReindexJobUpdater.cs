using System.Text.Json.Nodes;
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

    public Task<bool> TryCompleteAsync(
        string jobId,
        string terminalStatus,
        Func<BackgroundJob<ReindexJobDefinition>, CancellationToken, Task> beforeCommit,
        Action<BackgroundJob<ReindexJobDefinition>> update,
        CancellationToken cancellationToken) =>
        jobLock.ExecuteAsync(
            ct => TryCompleteUnderLockAsync(
                jobId,
                terminalStatus,
                beforeCommit,
                update,
                ct),
            cancellationToken);

    internal async Task<bool> TryCompleteUnderLockAsync(
        string jobId,
        string terminalStatus,
        Func<BackgroundJob<ReindexJobDefinition>, CancellationToken, Task> beforeCommit,
        Action<BackgroundJob<ReindexJobDefinition>> update,
        CancellationToken cancellationToken)
    {
        var job = await repository.GetAsync(jobId, GlobalTenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Reindex job {jobId} does not exist.");
        if (IsTerminal(job.Status))
        {
            return false;
        }

        if (job.Status == "Completing")
        {
            var persistedDecision = job.Progress?["terminalDecision"]?.GetValue<string>();
            if (!string.Equals(
                persistedDecision,
                terminalStatus,
                StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        else
        {
            update(job);
            if (!string.Equals(job.Status, terminalStatus, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Reindex terminal update selected '{job.Status}' instead of '{terminalStatus}'.");
            }

            job.Progress ??= new JsonObject();
            job.Progress["terminalDecision"] = terminalStatus;
            job.Status = "Completing";
            job.HeartbeatDate = DateTimeOffset.UtcNow;
            try
            {
                await repository.UpdateAsync(job, GlobalTenantId, cancellationToken);
            }
            catch (BackgroundJobUpdateConflictException)
            {
                return false;
            }
        }

        await beforeCommit(job, cancellationToken);
        update(job);
        if (!string.Equals(job.Status, terminalStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Reindex terminal update selected '{job.Status}' instead of '{terminalStatus}'.");
        }

        job.HeartbeatDate = DateTimeOffset.UtcNow;
        await completionHook.OnCompletedAsync(job, cancellationToken);
        try
        {
            await repository.UpdateAsync(job, GlobalTenantId, cancellationToken);
        }
        catch (BackgroundJobUpdateConflictException)
        {
            return false;
        }

        return true;
    }

    private static bool IsTerminal(string status) =>
        status is "Completed" or "Failed" or "Cancelled";
}
