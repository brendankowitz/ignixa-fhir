// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Domain.Abstractions;
using Ignixa.Application.BackgroundOperations.Import.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.Import.Activities;

/// <summary>
/// Updates import job progress in job store.
/// Phase 6: Progress tracking for long-running imports.
/// </summary>
public class UpdateProgressActivity : AsyncTaskActivity<UpdateProgressInput, bool>
{
    private readonly IImportJobStore _jobStore;
    private readonly ILogger<UpdateProgressActivity> _logger;

    public UpdateProgressActivity(
        IImportJobStore jobStore,
        ILogger<UpdateProgressActivity> logger)
    {
        _jobStore = jobStore ?? throw new ArgumentNullException(nameof(jobStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task<bool> ExecuteAsync(
        TaskContext context,
        UpdateProgressInput input)
    {
        try
        {
            var job = await _jobStore.GetJobAsync(input.TenantId, input.JobId, CancellationToken.None);

            if (job == null)
            {
                _logger.LogWarning("Job {JobId} not found for progress update", input.JobId);
                return false;
            }

            // Update progress fields
            job.ProcessedResources = input.ProcessedResources;
            job.ProcessedFiles = input.ProcessedFiles;
            job.CurrentFile = input.CurrentFile;

            // Calculate progress percentage
            // Use files as progress indicator (more accurate than resource count)
            job.ProgressPercentage = input.TotalFiles > 0
                ? Math.Round((double)input.ProcessedFiles / input.TotalFiles * 100, 2)
                : 0;

            // Update status to Running if not already
            if (job.Status == "Queued")
            {
                job.Status = "Running";
                job.StartDate = DateTimeOffset.UtcNow;
            }

            await _jobStore.UpdateJobAsync(job, CancellationToken.None);

            _logger.LogDebug(
                "Updated progress for job {JobId}: {ProcessedFiles}/{TotalFiles} files ({Percentage}%)",
                input.JobId,
                input.ProcessedFiles,
                input.TotalFiles,
                job.ProgressPercentage);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating progress for job {JobId}", input.JobId);
            return false;
        }
    }
}
