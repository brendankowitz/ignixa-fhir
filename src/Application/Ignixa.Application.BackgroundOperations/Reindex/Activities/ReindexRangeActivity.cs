using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.BackgroundOperations.Reindex.Workers;
using Ignixa.Application.Features.Conformance;

namespace Ignixa.Application.BackgroundOperations.Reindex.Activities;

public sealed class ReindexRangeActivity(
    ReindexRangeProcessor processor,
    ReindexProgressReporter progress)
    : AsyncTaskActivity<ReindexRangeInput, ReindexRangeOutput>
{
    protected override async Task<ReindexRangeOutput> ExecuteAsync(
        TaskContext context,
        ReindexRangeInput input)
    {
        ReindexMetrics.RangeStarted();
        try
        {
            var output = await progress.RunWithHeartbeatAsync(
                input.JobId, cancellationToken => processor.ProcessAsync(input, cancellationToken), CancellationToken.None);
            ReindexMetrics.RangeCompleted(output.ResourcesReindexed, output.Conflicts, output.FailedResources.Count);
            return output;
        }
        catch
        {
            ReindexMetrics.RangeFailed();
            throw;
        }
    }
}
