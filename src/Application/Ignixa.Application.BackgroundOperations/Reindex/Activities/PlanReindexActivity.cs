using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Domain.Abstractions;

namespace Ignixa.Application.BackgroundOperations.Reindex.Activities;

public sealed class PlanReindexActivity(
    IFhirRepositoryFactory repositoryFactory,
    ReindexProgressReporter progress)
    : AsyncTaskActivity<PlanReindexInput, PlanReindexOutput>
{
    protected override async Task<PlanReindexOutput> ExecuteAsync(
        TaskContext context,
        PlanReindexInput input)
    {
        var repository = await repositoryFactory.GetRepositoryAsync(
            input.TenantId,
            CancellationToken.None);
        if (repository is not IReindexStore store)
        {
            throw new ReindexProviderNotSupportedException(input.TenantId);
        }

        var page = await store.GetSurrogateIdRangesAsync(
            input.ResourceType,
            input.StartAfterSurrogateId,
            input.CutoffSurrogateId,
            input.TargetRangeSize,
            input.MaxRanges,
            CancellationToken.None);
        var output = new PlanReindexOutput(
            page.Ranges.Select(range => new ReindexRange(
                range.Start,
                range.End,
                range.ResourceCount)).ToArray(),
            page.NextStartAfter);
        await progress.ReportPlanAsync(input, output, CancellationToken.None);
        return output;
    }
}
