using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.Features.Conformance;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.Reindex.Activities;

public sealed class PersistReindexProgressActivity(
    ReindexProgressReporter progress,
    ILogger<PersistReindexProgressActivity> logger)
    : AsyncTaskActivity<PersistReindexProgressInput, bool>
{
    protected override async Task<bool> ExecuteAsync(TaskContext context, PersistReindexProgressInput input)
    {
        try
        {
            return await progress.ReportAsync(input, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ReindexMetrics.ProgressPersistenceFailed();
            logger.LogWarning(ex,
                "Reindex job {JobId} progress persistence failed at sequence {Sequence}; retrying progress independently of range work",
                input.JobId, input.Sequence);
            throw;
        }
    }
}
