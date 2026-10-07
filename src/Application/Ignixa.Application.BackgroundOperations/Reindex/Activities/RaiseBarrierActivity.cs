using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Domain.Abstractions;

namespace Ignixa.Application.BackgroundOperations.Reindex.Activities;

public sealed class RaiseBarrierActivity(
    IFhirRepositoryFactory repositoryFactory,
    ReindexProgressReporter progress)
    : AsyncTaskActivity<RaiseBarrierInput, RaiseBarrierOutput>
{
    protected override async Task<RaiseBarrierOutput> ExecuteAsync(
        TaskContext context,
        RaiseBarrierInput input)
    {
        var repository = await repositoryFactory.GetRepositoryAsync(
            input.TenantId,
            CancellationToken.None);
        if (repository is not IReindexStore store)
        {
            throw new ReindexProviderNotSupportedException(input.TenantId);
        }

        var cutoff = await store.RaiseBarrierAsync(
            input.TargetEventId,
            CancellationToken.None);
        var output = new RaiseBarrierOutput(
            input.TenantId,
            cutoff.TransactionId,
            cutoff.SurrogateId);
        await progress.ReportBarrierAsync(input.JobId, output, CancellationToken.None);
        return output;
    }
}
