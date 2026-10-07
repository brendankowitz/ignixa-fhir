using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.BackgroundOperations.Reindex.Workers;

namespace Ignixa.Application.BackgroundOperations.Reindex.Activities;

public sealed class ReindexRangeActivity(ReindexRangeProcessor processor)
    : AsyncTaskActivity<ReindexRangeInput, ReindexRangeOutput>
{
    protected override Task<ReindexRangeOutput> ExecuteAsync(
        TaskContext context,
        ReindexRangeInput input) =>
        processor.ProcessAsync(input, CancellationToken.None);
}
