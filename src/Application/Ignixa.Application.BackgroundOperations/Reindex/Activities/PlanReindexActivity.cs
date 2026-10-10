using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Domain.Abstractions;

namespace Ignixa.Application.BackgroundOperations.Reindex.Activities;

public sealed class PlanReindexActivity(
    IReindexStoreFactory reindexStoreFactory,
    ReindexProgressReporter progress)
    : AsyncTaskActivity<PlanReindexInput, PlanReindexOutput>
{
    // The store takes -1 for "from the start of the type"; the orchestration models that as no cursor.
    private const long StartOfType = -1;

    protected override Task<PlanReindexOutput> ExecuteAsync(
        TaskContext context,
        PlanReindexInput input) =>
        progress.RunWithHeartbeatAsync(
            input.JobId, cancellationToken => PlanAsync(input, cancellationToken), CancellationToken.None);

    private async Task<PlanReindexOutput> PlanAsync(PlanReindexInput input, CancellationToken cancellationToken)
    {
        var store = await reindexStoreFactory.GetReindexStoreAsync(
            input.TenantId,
            cancellationToken);

        var page = await store.GetSurrogateIdRangesAsync(
            input.ResourceType,
            input.StartAfterSurrogateId ?? StartOfType,
            input.CutoffSurrogateId,
            input.TargetRangeSize,
            input.MaxRanges,
            cancellationToken);
        return new PlanReindexOutput(
            page.Ranges.Select(range => new ReindexRange(
                range.Start,
                range.End,
                range.ResourceCount)).ToArray(),
            page.NextStartAfter);
    }
}
