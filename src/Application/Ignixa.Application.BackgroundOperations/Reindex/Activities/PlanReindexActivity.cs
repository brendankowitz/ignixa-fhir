using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Domain.Abstractions;

namespace Ignixa.Application.BackgroundOperations.Reindex.Activities;

public sealed class PlanReindexActivity(
    IFhirRepositoryFactory repositoryFactory,
    ReindexActivityHeartbeat heartbeat)
    : AsyncTaskActivity<PlanReindexInput, PlanReindexOutput>
{
    protected override Task<PlanReindexOutput> ExecuteAsync(
        TaskContext context,
        PlanReindexInput input) =>
        heartbeat.RunAsync(input.JobId, cancellationToken => PlanAsync(input, cancellationToken), CancellationToken.None);

    private async Task<PlanReindexOutput> PlanAsync(PlanReindexInput input, CancellationToken cancellationToken)
    {
        var store = await repositoryFactory.GetReindexStoreAsync(
            input.TenantId,
            cancellationToken);

        var page = await store.GetSurrogateIdRangesAsync(
            input.ResourceType,
            input.StartAfterSurrogateId,
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
