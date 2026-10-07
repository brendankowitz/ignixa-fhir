using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex.Activities;

public sealed class StartReindexActivity(
    ReindexLifecycleEventWriter lifecycle,
    ReindexJobUpdater jobs)
    : AsyncTaskActivity<StartReindexInput, StartReindexOutput>
{
    protected override async Task<StartReindexOutput> ExecuteAsync(
        TaskContext context,
        StartReindexInput input)
    {
        IReadOnlyList<string> ignored = [];
        await jobs.UpdateAsync(
            input.JobId,
            async (job, cancellationToken) =>
            {
                ignored = await lifecycle.StartAsync(
                    input.JobId,
                    input.Targets.Where(target => target.IsFullyCovered).ToArray(),
                    cancellationToken);
                ReindexProgressReporter.InitializeBarrierDelay(job, input.TenantIds, ignored);
            },
            CancellationToken.None);
        return new StartReindexOutput(ignored);
    }
}
