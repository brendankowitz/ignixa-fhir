using DurableTask.Core;
using Microsoft.Extensions.Logging;
using Ignixa.Domain.Abstractions;

namespace Ignixa.Application.BackgroundOperations.Export.Activities;

/// <summary>
/// DurableTask activity that updates the export job status to completed or failed.
/// </summary>
public class CompleteJobActivity : AsyncTaskActivity<CompleteJobInput, bool>
{
    private readonly IExportJobStore _jobStore;
    private readonly ILogger<CompleteJobActivity> _logger;

    public CompleteJobActivity(
        IExportJobStore jobStore,
        ILogger<CompleteJobActivity> logger)
    {
        _jobStore = jobStore ?? throw new ArgumentNullException(nameof(jobStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task<bool> ExecuteAsync(TaskContext context, CompleteJobInput input)
    {
        var job = await _jobStore.GetJobAsync(input.JobId, CancellationToken.None);
        if (job == null)
        {
            _logger.LogWarning("Job {JobId} not found", input.JobId);
            return false;
        }

        if (input.Success)
        {
            job.TotalResourcesExported = input.TotalResourcesExported;

            foreach (var (resourceType, filePath) in input.ExportedFiles)
            {
                var fileName = Path.GetFileName(filePath);
                job.AddOutputFile(resourceType, fileName);
            }

            job.MarkAsCompleted();
            _logger.LogInformation(
                "Job {JobId} completed successfully ({TotalResources} resources)",
                input.JobId,
                input.TotalResourcesExported);
        }
        else
        {
            job.MarkAsFailed(input.ErrorMessage ?? "Unknown error");
            _logger.LogError("Job {JobId} failed: {Error}", input.JobId, input.ErrorMessage);
        }

        await _jobStore.UpdateJobAsync(job, CancellationToken.None);
        return true;
    }
}

/// <summary>
/// Input for CompleteJobActivity.
/// </summary>
public record CompleteJobInput(
    string JobId,
    bool Success,
    Dictionary<string, string> ExportedFiles,
    int TotalResourcesExported,
    string? ErrorMessage);
