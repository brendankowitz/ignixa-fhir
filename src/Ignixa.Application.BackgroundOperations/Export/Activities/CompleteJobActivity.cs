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
        // Retrieve job with tenant validation
        var job = await _jobRepository.GetAsync(input.JobId, input.TenantId, CancellationToken.None);
        if (job == null)
        {
            _logger.LogWarning("Job {JobId} not found (tenant {TenantId})", input.JobId, input.TenantId);
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

            // Log throughput metrics if timing information is available
            if (job.StartDate.HasValue)
            {
                var elapsed = (job.EndDate.Value - job.StartDate.Value).TotalSeconds;
                if (elapsed > 0)
                {
                    var resourcesPerSec = input.TotalResourcesExported / elapsed;
                    _logger.LogInformation(
                        "Export job {JobId} completed successfully: {TotalResources} resources in {ElapsedSeconds:F2}s = {ThroughputPerSec:F2} resources/sec",
                        input.JobId,
                        input.TotalResourcesExported,
                        elapsed,
                        resourcesPerSec);
                }
                else
                {
                    _logger.LogInformation(
                        "Job {JobId} completed successfully ({TotalResources} resources)",
                        input.JobId,
                        input.TotalResourcesExported);
                }
            }
            else
            {
                _logger.LogInformation(
                    "Job {JobId} completed successfully ({TotalResources} resources)",
                    input.JobId,
                    input.TotalResourcesExported);
            }
        }
        else
        {
            job.Status = "Failed";
            job.EndDate = DateTimeOffset.UtcNow;
            job.ErrorMessage = input.ErrorMessage ?? "Unknown error";

            _logger.LogError("Job {JobId} failed: {Error}", input.JobId, input.ErrorMessage);
        }

        await _jobRepository.UpdateAsync(job, input.TenantId, CancellationToken.None);
        return true;
    }
}

/// <summary>
/// Input for CompleteJobActivity.
/// </summary>
public record CompleteJobInput(
    string JobId,
    int TenantId,
    bool Success,
    Dictionary<string, string> ExportedFiles,
    int TotalResourcesExported,
    string? ErrorMessage);
