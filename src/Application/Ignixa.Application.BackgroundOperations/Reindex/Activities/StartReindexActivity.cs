using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex.Activities;

public sealed class StartReindexActivity(
    ReindexLifecycleEventWriter lifecycle,
    ReindexProgressReporter progress)
    : AsyncTaskActivity<StartReindexInput, StartReindexOutput>
{
    protected override async Task<StartReindexOutput> ExecuteAsync(
        TaskContext context,
        StartReindexInput input)
    {
        var ignored = await lifecycle.StartAsync(
            input.JobId,
            input.Targets,
            CancellationToken.None);
        await progress.ReportBarrierDelayAsync(
            input.JobId,
            input.TenantIds,
            ignored,
            CancellationToken.None);
        return new StartReindexOutput(ignored);
    }
}
