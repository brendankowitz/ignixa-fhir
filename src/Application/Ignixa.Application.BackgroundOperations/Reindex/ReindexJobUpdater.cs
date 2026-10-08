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

    public async Task<bool> UpdateProgressAsync(
        string jobId,
        Action<BackgroundJob<ReindexJobDefinition>> update,
        CancellationToken cancellationToken)
    {
        const int maximumAttempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            var job = await repository.GetAsync(jobId, GlobalTenantId, cancellationToken)
                ?? throw new InvalidOperationException($"Reindex job {jobId} does not exist.");
            if (IsClosed(job.Status))
            {
                return false;
            }

            update(job);
            try
            {
                return await repository.TryUpdateProgressAsync(job, GlobalTenantId, cancellationToken);
            }
            catch (BackgroundJobUpdateConflictException) when (attempt < maximumAttempts)
            {
                // Reload and merge again; never replay a stale whole-job snapshot.
            }
        }
    }

    public Task UpdateAsync(
        string jobId,
        Func<BackgroundJob<ReindexJobDefinition>, CancellationToken, Task> update,
        CancellationToken cancellationToken) =>
        jobLock.ExecuteAsync(
            async ct =>
            {
                var job = await repository.GetAsync(jobId, GlobalTenantId, ct)
                    ?? throw new InvalidOperationException($"Reindex job {jobId} does not exist.");
                // Completing owns a durable decision; only terminal completion may write it again.
                if (IsClosed(job.Status))
                {
                    return false;
                }

                await update(job, ct);
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

    public Task<bool> TryCompleteAsync(
        string jobId,
        Func<BackgroundJob<ReindexJobDefinition>, CancellationToken, Task<string>> selectTerminalStatus,
        Func<BackgroundJob<ReindexJobDefinition>, CancellationToken, Task> beforeCommit,
        Action<BackgroundJob<ReindexJobDefinition>> update,
        CancellationToken cancellationToken) =>
        jobLock.ExecuteAsync(
            async ct =>
            {
                var job = await repository.GetAsync(jobId, GlobalTenantId, ct)
                    ?? throw new InvalidOperationException($"Reindex job {jobId} does not exist.");
                var terminalStatus = await selectTerminalStatus(job, ct);
                return await TryCompleteUnderLockAsync(
                    job,
                    terminalStatus,
                    beforeCommit,
                    update,
                    ct);
            },
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
        return await TryCompleteUnderLockAsync(
            job,
            terminalStatus,
            beforeCommit,
            update,
            cancellationToken);
    }

    private async Task<bool> TryCompleteUnderLockAsync(
        BackgroundJob<ReindexJobDefinition> job,
        string terminalStatus,
        Func<BackgroundJob<ReindexJobDefinition>, CancellationToken, Task> beforeCommit,
        Action<BackgroundJob<ReindexJobDefinition>> update,
        CancellationToken cancellationToken)
    {
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
        status.Equals("Completed", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("Failed", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase);

    private static bool IsClosed(string status) =>
        status.Equals("Completing", StringComparison.OrdinalIgnoreCase) || IsTerminal(status);
}
