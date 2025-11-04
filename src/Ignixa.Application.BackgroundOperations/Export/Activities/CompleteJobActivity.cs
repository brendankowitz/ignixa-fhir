using System.Text.Json;
using System.Text.Json.Nodes;
using DurableTask.Core;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.Export.Activities;

/// <summary>
/// DurableTask activity that updates the export job status to completed or failed.
/// Uses the unified IBackgroundJobRepository<ExportJobDefinition> for storage.
/// </summary>
public class CompleteJobActivity : AsyncTaskActivity<CompleteJobInput, bool>
{
    private readonly IBackgroundJobRepository<ExportJobDefinition> _jobRepository;
    private readonly ILogger<CompleteJobActivity> _logger;

    public CompleteJobActivity(
        IBackgroundJobRepository<ExportJobDefinition> jobRepository,
        ILogger<CompleteJobActivity> logger)
    {
        _jobRepository = jobRepository ?? throw new ArgumentNullException(nameof(jobRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task<bool> ExecuteAsync(TaskContext context, CompleteJobInput input)
    {
        // Note: We need tenantId but it's not in the input. For now, use a default of 1.
        // This activity is currently not called by the export orchestration,
        // but is kept here for future use when direct activity calling is needed.
        var tenantId = 1; // TODO: Pass tenantId from orchestration input

        var job = await _jobRepository.GetAsync(tenantId, input.JobId, CancellationToken.None);
        if (job == null)
        {
            _logger.LogWarning("Job {JobId} not found", input.JobId);
            return false;
        }

        if (input.Success)
        {
            // Update result with export completion information
            job.Result = JsonNode.Parse(JsonSerializer.Serialize(new
            {
                totalResources = input.TotalResourcesExported,
                exportedFiles = input.ExportedFiles,
                completedAt = DateTimeOffset.UtcNow
            }));

            job.Status = "Completed";
            job.EndDate = DateTimeOffset.UtcNow;

            _logger.LogInformation(
                "Job {JobId} completed successfully ({TotalResources} resources)",
                input.JobId,
                input.TotalResourcesExported);
        }
        else
        {
            job.Status = "Failed";
            job.EndDate = DateTimeOffset.UtcNow;
            job.ErrorMessage = input.ErrorMessage ?? "Unknown error";

            _logger.LogError("Job {JobId} failed: {Error}", input.JobId, input.ErrorMessage);
        }

        await _jobRepository.UpdateAsync(job, CancellationToken.None);
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
