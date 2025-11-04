// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using DurableTask.Core;
using Ignixa.Domain.Abstractions;
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
        try
        {
            var job = await _jobRepository.GetAsync(input.JobId, input.TenantId, CancellationToken.None);

            if (job == null)
            {
                _logger.LogWarning("Job {JobId} not found for progress update (tenant {TenantId})", input.JobId, input.TenantId);
                return false;
            }

            // Calculate progress percentage
            // Use files as progress indicator (more accurate than resource count)
            var progressPercentage = input.TotalFiles > 0
                ? Math.Round((double)input.ProcessedFiles / input.TotalFiles * 100, 2)
                : 0;

            // Update progress as JSON using strongly-typed POCO
            var progress = new ImportJobProgress
            {
                ProcessedResources = input.ProcessedResources,
                ProcessedFiles = input.ProcessedFiles,
                CurrentFile = input.CurrentFile,
                ProgressPercentage = progressPercentage
            };

            job.Progress = JsonNode.Parse(JsonSerializer.Serialize(progress));

            // Update status to Running if not already
            if (job.Status == "Queued")
            {
                job.Status = "Running";
                job.StartDate = DateTimeOffset.UtcNow;
            }

            await _jobRepository.UpdateAsync(job, input.TenantId, CancellationToken.None);

            _logger.LogDebug(
                "Updated progress for job {JobId}: {ProcessedFiles}/{TotalFiles} files ({Percentage}%)",
                input.JobId,
                input.ProcessedFiles,
                input.TotalFiles,
                progressPercentage);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating progress for job {JobId}", input.JobId);
            return false;
        }
    }
}
