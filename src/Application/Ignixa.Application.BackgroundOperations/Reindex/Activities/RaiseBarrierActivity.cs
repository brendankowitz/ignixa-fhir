using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex.Activities;

public sealed class RaiseBarrierActivity(
    IReindexStoreFactory reindexStoreFactory,
    ReindexProgressReporter progress)
    : AsyncTaskActivity<RaiseBarrierInput, BarrierCutoff>
{
    protected override Task<BarrierCutoff> ExecuteAsync(
        TaskContext context,
        RaiseBarrierInput input) =>
        progress.RunWithHeartbeatAsync(
            input.JobId, cancellationToken => RaiseAsync(input, cancellationToken), CancellationToken.None);

    private async Task<BarrierCutoff> RaiseAsync(RaiseBarrierInput input, CancellationToken cancellationToken)
    {
        var store = await reindexStoreFactory.GetReindexStoreAsync(
            input.TenantId,
            cancellationToken);

        return await store.RaiseBarrierAsync(input.TargetEventId, cancellationToken);
    }
}
