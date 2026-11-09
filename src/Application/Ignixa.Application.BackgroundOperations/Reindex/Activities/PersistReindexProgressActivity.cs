using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.Features.Conformance;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.Reindex.Activities;

/// <summary>
/// Persists one progress snapshot. Returns false when the job row is already closed or gone, which tells the
/// orchestration to stop; a storage failure propagates so the orchestration's bounded retry applies.
/// </summary>
public sealed class PersistReindexProgressActivity(
    ReindexProgressReporter progress,
    ILogger<PersistReindexProgressActivity> logger)
    : AsyncTaskActivity<PersistReindexProgressInput, bool>
{
    protected override async Task<bool> ExecuteAsync(TaskContext context, PersistReindexProgressInput input)
    {
        try
        {
            return await progress.ReportAsync(input.JobId, input.Progress, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ReindexMetrics.ProgressPersistenceFailed();
            logger.LogWarning(ex,
                "Reindex job {JobId} progress persistence failed in phase {Phase}; retrying progress independently of range work",
                input.JobId, input.Progress.Phase);
            throw;
        }
    }
}
