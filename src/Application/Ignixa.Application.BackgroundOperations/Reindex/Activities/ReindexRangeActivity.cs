using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.BackgroundOperations.Reindex.Workers;

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
            var output = await processor.ProcessAsync(input, CancellationToken.None);
            await progress.ReportRangeAsync(input, output, CancellationToken.None);
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
