using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.BackgroundOperations.Reindex.Workers;

namespace Ignixa.Application.BackgroundOperations.Reindex.Activities;

public sealed class ReindexRangeActivity(
    ReindexRangeProcessor processor,
    ReindexActivityHeartbeat heartbeat)
    : AsyncTaskActivity<ReindexRangeInput, ReindexRangeOutput>
{
    protected override async Task<ReindexRangeOutput> ExecuteAsync(
        TaskContext context,
        ReindexRangeInput input)
    {
        ReindexMetrics.RangeStarted();
        try
        {
            var output = await heartbeat.RunAsync(
                input.JobId, cancellationToken => processor.ProcessAsync(input, cancellationToken), CancellationToken.None);
            ReindexMetrics.RangeCompleted(output);
            return output;
        }
        catch
        {
            ReindexMetrics.RangeFailed();
            throw;
        }
    }
}
