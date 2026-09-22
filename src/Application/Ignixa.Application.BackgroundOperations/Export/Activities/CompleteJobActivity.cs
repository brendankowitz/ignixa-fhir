using System.Text.Json;
using DurableTask.Core;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
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
            throw new InvalidOperationException($"Export job {input.JobId} not found for tenant {input.TenantId}.");
        }

        if (job.Status is "Cancelled" or "Failed" or "Completed")
        {
            _logger.LogInformation("Export completion for {JobId} is superseded by {Status}", input.JobId, job.Status);
            return job.Status == "Completed";
        }

        if (input.Success)
        {
            // Update result with export completion information
            job.Result = JsonSerializer.SerializeToNode(new ExportJobResult
            {
                TotalResources = input.TotalResourcesExported,
                ExportedFiles = input.ExportedFiles,
                ExportedFileCounts = input.ExportedFileCounts,
                CompletedAt = DateTimeOffset.UtcNow
            });

            job.Status = "Completed";
            job.EndDate = DateTimeOffset.UtcNow;

        }
        else
        {
            job.Status = "Failed";
            job.EndDate = DateTimeOffset.UtcNow;
            job.ErrorMessage = input.ErrorMessage ?? "Unknown error";
        }

        try
        {
            await _jobRepository.UpdateAsync(job, input.TenantId, CancellationToken.None);
        }
        catch (BackgroundJobUpdateConflictException)
        {
            var authoritative = await _jobRepository.GetAsync(input.JobId, input.TenantId, CancellationToken.None)
                ?? throw new InvalidOperationException($"Export job {input.JobId} disappeared after a terminal update conflict.");
            _logger.LogInformation(
                "Export completion for {JobId} was superseded; preserving {Status}",
                input.JobId, authoritative.Status);
            return authoritative.Status == "Completed";
        }

        if (input.Success)
        {
            var elapsed = job.StartDate.HasValue ? (job.EndDate!.Value - job.StartDate.Value).TotalSeconds : (double?)null;
            _logger.LogInformation(
                "Export job {JobId} completed: {TotalResources} resources, elapsed {ElapsedSeconds}s",
                input.JobId, input.TotalResourcesExported, elapsed);
        }
        else
        {
            _logger.LogError("Job {JobId} failed: {Error}", input.JobId, input.ErrorMessage);
        }
        return input.Success;
    }
}
