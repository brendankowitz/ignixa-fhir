using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.BackgroundOperations.Reindex;

/// <summary>
/// Recovers the active reindex job when its orchestration is missing or already terminal. One rule: the job
/// is Completed when the projection already shows its completion (the events were appended before the final
/// status write was lost); otherwise guarded Failed events return every parameter it still owns to Pending
/// and the job is Failed. A Queued job whose orchestration was never
/// created is deleted, so it can neither block the start rule nor answer <c>$reindex</c> with 409.
/// </summary>
public sealed class ReindexJobReconciler(
    TaskHubClient taskHubClient,
    IBackgroundJobRepository<ReindexJobDefinition> repository,
    ReindexLifecycleEventWriter lifecycle,
    IReindexJobLock jobLock,
    IOptions<ReindexOptions> options,
    TimeProvider timeProvider,
    ILogger<ReindexJobReconciler> logger)
{
    private static readonly EventId StaleJob = new(4203, "ReindexStaleJob");

    /// <summary>
    /// Recovers <paramref name="active"/> once it has gone unobserved for <see cref="ReindexOptions.OrphanGrace"/>.
    /// Re-reads the job under the singleton lock, so a completion that lands first wins and nothing is applied twice.
    /// </summary>
    public async Task ReconcileAsync(
        BackgroundJob<ReindexJobDefinition> active,
        CancellationToken cancellationToken)
    {
        var lastObserved = active.HeartbeatDate > active.CreateDate
            ? active.HeartbeatDate
            : active.CreateDate;
        if (timeProvider.GetUtcNow() - lastObserved < options.Value.OrphanGrace)
        {
            return;
        }

        await jobLock.ExecuteAsync(
            async ct =>
            {
                await ReconcileUnderLockAsync(active.JobId, ct);
                return true;
            },
            cancellationToken);
    }

    private async Task ReconcileUnderLockAsync(string jobId, CancellationToken cancellationToken)
    {
        var job = await repository.GetAsync(jobId, SystemConstants.GlobalTenantId, cancellationToken);
        if (job is null || job.IsTerminal())
        {
            return;
        }

        var instanceId = job.OrchestrationInstanceId ?? job.JobId;
        var state = await taskHubClient.GetOrchestrationStateAsync(instanceId);
        if (IsActive(state))
        {
            ReportIfStale(job);
            return;
        }

        // The runtime can register the instance after the job row became visible; a second read is cheap.
        var confirmed = await taskHubClient.GetOrchestrationStateAsync(instanceId);
        if (IsActive(confirmed))
        {
            return;
        }

        state = confirmed ?? state;
        if (state is null && job.GetStatus() == ReindexJobStatus.Queued)
        {
            await repository.DeleteAsync(job.JobId, SystemConstants.GlobalTenantId, cancellationToken);
            logger.LogWarning(
                "Reindex: deleted queued job {JobId} because its orchestration was never created",
                job.JobId);
            return;
        }

        await FinalizeAsync(job, state, cancellationToken);
    }

    // A running orchestration that stopped heartbeating is hung, not orphaned: report it for an operator.
    private void ReportIfStale(BackgroundJob<ReindexJobDefinition> job)
    {
        if (job.IsStale(ReindexProgress.FromJson(job.Progress), options.Value, timeProvider.GetUtcNow()))
        {
            ReindexMetrics.StaleJobObserved();
            logger.LogError(
                StaleJob,
                "Reindex: job {JobId} is running but has not heartbeated since {HeartbeatDate}",
                job.JobId,
                job.HeartbeatDate);
        }
    }

    private async Task FinalizeAsync(
        BackgroundJob<ReindexJobDefinition> job,
        OrchestrationState? state,
        CancellationToken cancellationToken)
    {
        var reason = state is null
            ? "Reindex orchestration instance is missing."
            : $"Reindex orchestration ended as {state.OrchestrationStatus} before the job was finalized.";
        var now = timeProvider.GetUtcNow();
        if (await lifecycle.HasCompletedAsync(job.JobId, job.Definition.SearchParameters, cancellationToken))
        {
            job.Close(ReindexJobStatus.Completed, null, now);
            logger.LogInformation(
                "Reindex: finalized job {JobId} as Completed; its targets were already enabled. {Reason}",
                job.JobId,
                reason);
        }
        else
        {
            await lifecycle.FinishAsync(job.JobId, [], reason, TimeSpan.Zero, cancellationToken);
            job.Close(ReindexJobStatus.Failed, reason, now);
            logger.LogError(
                "Reindex: finalized orphaned job {JobId} as Failed: {Reason}",
                job.JobId,
                reason);
        }

        await repository.UpdateAsync(job, SystemConstants.GlobalTenantId, cancellationToken);
    }

    private static bool IsActive(OrchestrationState? state) =>
        state?.OrchestrationStatus is OrchestrationStatus.Pending
            or OrchestrationStatus.Running
            or OrchestrationStatus.ContinuedAsNew;
}
