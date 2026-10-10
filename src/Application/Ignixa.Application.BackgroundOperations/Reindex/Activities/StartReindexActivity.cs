using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex.Activities;

/// <summary>
/// Takes ownership of the job's targets: under the singleton job lock it appends the guarded Started
/// events and moves the job to Running. A job that was finished while it waited (cancelled, recovered)
/// is left alone and the orchestration is told not to continue.
/// </summary>
public sealed class StartReindexActivity(
    ReindexLifecycleEventWriter lifecycle,
    IBackgroundJobRepository<ReindexJobDefinition> repository,
    IReindexJobLock jobLock,
    TimeProvider timeProvider)
    : AsyncTaskActivity<StartReindexInput, StartReindexOutput>
{
    protected override Task<StartReindexOutput> ExecuteAsync(
        TaskContext context,
        StartReindexInput input) =>
        jobLock.ExecuteAsync(
            cancellationToken => StartUnderLockAsync(input, cancellationToken),
            CancellationToken.None);

    private async Task<StartReindexOutput> StartUnderLockAsync(
        StartReindexInput input,
        CancellationToken cancellationToken)
    {
        var job = await repository.GetAsync(input.JobId, SystemConstants.GlobalTenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Reindex job {input.JobId} does not exist.");
        if (job.IsTerminal())
        {
            return new StartReindexOutput([])
            {
                ShouldContinue = false,
                TargetEventId = input.TargetEventId,
                Targets = input.Targets
            };
        }

        var usesPersistedDefinition =
            job.Definition.SearchParameters.Count > 0 || input.Targets.Count == 0;
        var targets = usesPersistedDefinition ? job.Definition.SearchParameters : input.Targets;
        var ignored = await lifecycle.StartAsync(input.JobId, targets, cancellationToken);
        var now = timeProvider.GetUtcNow();
        job.SetStatus(ReindexJobStatus.Running);
        job.StartDate ??= now;
        job.HeartbeatDate = now;
        job.Progress = (ReindexProgress.Create(input.TenantIds) with { IgnoredLifecycleEvents = ignored }).ToJson();
        await repository.UpdateAsync(job, SystemConstants.GlobalTenantId, cancellationToken);

        return new StartReindexOutput(ignored)
        {
            ShouldContinue = true,
            TargetEventId = usesPersistedDefinition ? job.Definition.TargetEventId : input.TargetEventId,
            ResourceTypes = usesPersistedDefinition ? job.Definition.ResourceTypes : null,
            Targets = targets
        };
    }
}
