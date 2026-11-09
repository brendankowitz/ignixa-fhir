using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex.Activities;

public sealed class PlanReindexActivity(
    IReindexStoreFactory reindexStoreFactory,
    ReindexProgressReporter progress)
    : AsyncTaskActivity<PlanReindexInput, SurrogateIdRangePage>
{
    // The store takes -1 for "from the start of the type"; the orchestration models that as no cursor.
    private const long StartOfType = -1;

    protected override Task<SurrogateIdRangePage> ExecuteAsync(
        TaskContext context,
        PlanReindexInput input) =>
        progress.RunWithHeartbeatAsync(
            input.JobId, cancellationToken => PlanAsync(input, cancellationToken), CancellationToken.None);

    private async Task<SurrogateIdRangePage> PlanAsync(PlanReindexInput input, CancellationToken cancellationToken)
    {
        var store = await reindexStoreFactory.GetReindexStoreAsync(
            input.TenantId,
            cancellationToken);

        return await store.GetSurrogateIdRangesAsync(
            input.ResourceType,
            input.StartAfterSurrogateId ?? StartOfType,
            input.CutoffSurrogateId,
            input.TargetRangeSize,
            input.MaxRanges,
            cancellationToken);
    }
}
