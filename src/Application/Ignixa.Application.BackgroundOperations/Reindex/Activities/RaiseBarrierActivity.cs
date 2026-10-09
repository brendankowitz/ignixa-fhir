using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Domain.Abstractions;

namespace Ignixa.Application.BackgroundOperations.Reindex.Activities;

public sealed class RaiseBarrierActivity(
    IFhirRepositoryFactory repositoryFactory,
    ReindexActivityHeartbeat heartbeat)
    : AsyncTaskActivity<RaiseBarrierInput, RaiseBarrierOutput>
{
    protected override Task<RaiseBarrierOutput> ExecuteAsync(
        TaskContext context,
        RaiseBarrierInput input) =>
        heartbeat.RunAsync(input.JobId, cancellationToken => RaiseAsync(input, cancellationToken), CancellationToken.None);

    private async Task<RaiseBarrierOutput> RaiseAsync(RaiseBarrierInput input, CancellationToken cancellationToken)
    {
        var store = await repositoryFactory.GetReindexStoreAsync(
            input.TenantId,
            cancellationToken);

        var cutoff = await store.RaiseBarrierAsync(
            input.TargetEventId,
            cancellationToken);
        return new RaiseBarrierOutput(
            input.TenantId,
            cutoff.TransactionId,
            cutoff.SurrogateId);
    }
}
