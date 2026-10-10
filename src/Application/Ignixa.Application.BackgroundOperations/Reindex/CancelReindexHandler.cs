using System.Text.Json.Nodes;
using DurableTask.Core;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Domain.Models;
using Medino;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed class CancelReindexHandler(
    TaskHubClient taskHubClient,
    IBackgroundJobRepository<ReindexJobDefinition> repository,
    ReindexLifecycleEventWriter lifecycle,
    IReindexJobLock jobLock,
    TimeProvider timeProvider)
    : IRequestHandler<CancelReindexCommand, CancelReindexResult>
{
    public async Task<CancelReindexResult> HandleAsync(
        CancelReindexCommand request,
        CancellationToken cancellationToken)
    {
        // Job ids are shared by every job type; a job of another type is reported as absent.
        var job = await repository.GetAsync(
            request.JobId,
            SystemConstants.GlobalTenantId,
            (int)BackgroundJobType.Reindex,
            cancellationToken);
        if (job is null)
        {
            return new ReindexJobNotFoundResult(request.JobId);
        }

        if (job.IsTerminal())
        {
            return new ReindexJobAlreadyTerminalResult(job.JobId, job.GetStatus());
        }

        await taskHubClient.TerminateInstanceAsync(
            new OrchestrationInstance { InstanceId = job.OrchestrationInstanceId ?? job.JobId },
            request.Reason);
        var cancelled = await jobLock.ExecuteAsync(
            ct => CancelUnderLockAsync(request, ct),
            cancellationToken);
        if (cancelled)
        {
            return new ReindexCancelledResult(job.JobId);
        }

        var terminal = await repository.GetAsync(request.JobId, SystemConstants.GlobalTenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Reindex job {request.JobId} disappeared during cancellation.");
        return new ReindexJobAlreadyTerminalResult(terminal.JobId, terminal.GetStatus());
    }

    // Cancellation returns the job's parameters to Pending before the final write, like every other terminal
    // path, so a job can never finish while still owning a parameter.
    private async Task<bool> CancelUnderLockAsync(CancelReindexCommand request, CancellationToken cancellationToken)
    {
        var job = await repository.GetAsync(request.JobId, SystemConstants.GlobalTenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Reindex job {request.JobId} disappeared during cancellation.");
        if (job.IsTerminal())
        {
            return false;
        }

        var reason = $"Cancelled: {request.Reason}";
        await lifecycle.FailOwnedAsync(job.JobId, reason, cancellationToken);

        var now = timeProvider.GetUtcNow();
        job.SetStatus(ReindexJobStatus.Cancelled);
        job.CancelRequested = true;
        job.EndDate = now;
        job.HeartbeatDate = now;
        job.ErrorMessage = reason;
        job.Progress ??= new JsonObject();
        job.Progress["cancellationReason"] = request.Reason;
        await repository.UpdateAsync(job, SystemConstants.GlobalTenantId, cancellationToken);
        return true;
    }
}
