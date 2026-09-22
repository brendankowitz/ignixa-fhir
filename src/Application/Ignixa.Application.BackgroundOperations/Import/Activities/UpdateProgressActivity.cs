// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using DurableTask.Core;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Application.BackgroundOperations.Import.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.Import.Activities;

/// <summary>
/// Updates import job progress in job repository.
/// Phase 6: Progress tracking for long-running imports.
/// </summary>
public class UpdateProgressActivity : AsyncTaskActivity<UpdateProgressInput, bool>
{
    private readonly IBackgroundJobRepository<ImportJobDefinition> _jobRepository;
    private readonly ILogger<UpdateProgressActivity> _logger;

    public UpdateProgressActivity(
        IBackgroundJobRepository<ImportJobDefinition> jobRepository,
        ILogger<UpdateProgressActivity> logger)
    {
        _jobRepository = jobRepository ?? throw new ArgumentNullException(nameof(jobRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task<bool> ExecuteAsync(
        TaskContext context,
        UpdateProgressInput input)
    {
        var job = await _jobRepository.GetAsync(input.JobId, input.TenantId, CancellationToken.None)
            ?? throw new InvalidOperationException($"Import job {input.JobId} not found for progress update.");

        if (job.Status is "Completed" or "Failed" or "Cancelled")
        {
            _logger.LogInformation("Import progress for {JobId} is superseded by {Status}", input.JobId, job.Status);
            return false;
        }

        var progressPercentage = input.TotalFiles > 0
            ? Math.Round((double)input.ProcessedFiles / input.TotalFiles * 100, 2)
            : 0;

        var progress = new ImportJobProgress
        {
            ProcessedResources = input.ProcessedResources,
            ProcessedFiles = input.ProcessedFiles,
            CurrentFile = input.CurrentFile,
            ProgressPercentage = progressPercentage
        };

        job.Progress = JsonSerializer.SerializeToNode(progress);

        if (job.Status == "Queued")
        {
            job.Status = "Running";
            job.StartDate = DateTimeOffset.UtcNow;
        }

        try
        {
            await _jobRepository.UpdateAsync(job, input.TenantId, CancellationToken.None);
        }
        catch (BackgroundJobUpdateConflictException)
        {
            var authoritative = await _jobRepository.GetAsync(input.JobId, input.TenantId, CancellationToken.None)
                ?? throw new InvalidOperationException($"Import job {input.JobId} disappeared after a terminal update conflict.");
            _logger.LogInformation(
                "Import progress for {JobId} was superseded; preserving {Status}",
                input.JobId, authoritative.Status);
            return false;
        }

        _logger.LogDebug(
            "Updated progress for job {JobId}: {ProcessedFiles}/{TotalFiles} files ({Percentage}%)",
            input.JobId, input.ProcessedFiles, input.TotalFiles, progressPercentage);
        return true;
    }
}
