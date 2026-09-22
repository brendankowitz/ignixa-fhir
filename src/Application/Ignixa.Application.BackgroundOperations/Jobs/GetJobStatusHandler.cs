// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Export.Models;
using Ignixa.Application.BackgroundOperations.Import.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Medino;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Application.BackgroundOperations.Jobs;

/// <summary>
/// Handler for retrieving job status information.
/// Queries DurableTask orchestration state and updates job metadata accordingly.
/// Supports both Import and Export job types.
/// </summary>
public class GetJobStatusHandler : IRequestHandler<GetJobStatusQuery, GetJobStatusResult>
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly TaskHubClient _taskHubClient;
    private readonly IBackgroundJobRepository<ImportJobDefinition> _importRepository;
    private readonly IBackgroundJobRepository<ExportJobDefinition> _exportRepository;
    private readonly ILogger<GetJobStatusHandler> _logger;

    public GetJobStatusHandler(
        TaskHubClient taskHubClient,
        IBackgroundJobRepository<ImportJobDefinition> importRepository,
        IBackgroundJobRepository<ExportJobDefinition> exportRepository,
        ILogger<GetJobStatusHandler>? logger = null)
    {
        _taskHubClient = taskHubClient ?? throw new ArgumentNullException(nameof(taskHubClient));
        _importRepository = importRepository ?? throw new ArgumentNullException(nameof(importRepository));
        _exportRepository = exportRepository ?? throw new ArgumentNullException(nameof(exportRepository));
        _logger = logger ?? NullLogger<GetJobStatusHandler>.Instance;
    }

    public async Task<GetJobStatusResult> HandleAsync(
        GetJobStatusQuery request,
        CancellationToken cancellationToken)
    {
        // Validate job type
        if (request.JobType != "Import" && request.JobType != "Export")
        {
            throw new ArgumentException(
                $"Invalid jobType '{request.JobType}'. Must be 'Import' or 'Export'.",
                nameof(request));
        }

        // Get job metadata based on type
        string status;
        DateTimeOffset createDate;
        DateTimeOffset? startDate;
        DateTimeOffset? endDate;
        string? errorMessage;
        object? definition;
        object? result;
        double? progressPercentage = null;
        string? progressDescription = null;

        if (request.JobType == "Import")
        {
            var job = await GetCurrentJobAsync(_importRepository, request, cancellationToken);

            status = job.Status;
            createDate = job.CreateDate;
            startDate = job.StartDate;
            endDate = job.EndDate;
            errorMessage = job.ErrorMessage;

            definition = job.Definition;

            // Parse progress if available
            if (job.Progress != null)
            {
                var progress = job.Progress.Deserialize<ImportJobProgress>(SerializerOptions);
                progressPercentage = progress?.ProgressPercentage;
                progressDescription = progress != null
                    ? $"{progress.ProgressPercentage:F2}% ({progress.ProcessedFiles} files, {progress.ProcessedResources} resources)"
                    : status;
            }

            // Parse result if available
            if (job.Status == "Completed")
            {
                var importResult = job.Result?.Deserialize<ImportJobResult>(SerializerOptions)
                    ?? throw new JsonException("Completed import job has no persisted result.");
                if (importResult.TotalResources < 0 || importResult.TotalErrors < 0 ||
                    !importResult.CountsAreComplete || importResult.ResourcesWithUnknownOutcome != 0 ||
                    (importResult.TotalErrors > 0 && string.IsNullOrWhiteSpace(importResult.ErrorFileUrl)))
                {
                    throw new JsonException("Persisted import result has invalid counts or no error log.");
                }
                result = importResult;
            }
            else
            {
                result = null;
            }
        }
        else // Export
        {
            var job = await GetCurrentJobAsync(_exportRepository, request, cancellationToken);

            status = job.Status;
            createDate = job.CreateDate;
            startDate = job.StartDate;
            endDate = job.EndDate;
            errorMessage = job.ErrorMessage;

            definition = job.Definition;

            // Parse progress if available
            if (job.Progress != null)
            {
                var progress = job.Progress.Deserialize<ExportJobProgress>(SerializerOptions);
                progressPercentage = progress?.ProgressPercentage;
                progressDescription = progress != null
                    ? $"{progress.ProgressPercentage:F2}% ({progress.ResourcesExported} resources)"
                    : status;
            }

            // Parse result if available
            if (job.Status == "Completed")
            {
                var exportResult = job.Result?.Deserialize<ExportJobResult>(SerializerOptions)
                    ?? throw new JsonException("Completed export job has no persisted result.");
                if (exportResult.TotalResources < 0 || exportResult.ExportedFiles == null ||
                    (exportResult.TotalResources > 0 && exportResult.ExportedFiles.Count == 0) ||
                    exportResult.ExportedFiles.Any(file => string.IsNullOrWhiteSpace(file.Key) || string.IsNullOrWhiteSpace(file.Value)))
                {
                    throw new JsonException("Persisted export result has invalid counts or output files.");
                }
                if (exportResult.ExportedFileCounts is { } counts &&
                    (counts.Count != exportResult.ExportedFiles.Count ||
                     counts.Any(file => file.Value < 0 || !exportResult.ExportedFiles.ContainsKey(file.Key)) ||
                     counts.Values.Sum() != exportResult.TotalResources))
                {
                    throw new JsonException("Persisted export file counts do not match the total.");
                }
                result = exportResult;
            }
            else
            {
                result = null;
            }
        }

        return new GetJobStatusResult
        {
            JobId = request.JobId,
            JobType = request.JobType,
            Status = status,
            ProgressPercentage = progressPercentage,
            ProgressDescription = progressDescription ?? status,
            CreateDate = createDate,
            StartDate = startDate,
            EndDate = endDate,
            ErrorMessage = errorMessage,
            Definition = definition,
            Result = result
        };
    }

    private async Task<BackgroundJob<T>> GetCurrentJobAsync<T>(
        IBackgroundJobRepository<T> repository,
        GetJobStatusQuery request,
        CancellationToken cancellationToken)
        where T : class, IJobDefinition
    {
        var job = await repository.GetAsync(request.JobId, request.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException($"{request.JobType} job '{request.JobId}' not found for tenant {request.TenantId}");
        // Terminal metadata is authoritative even after orchestration history is purged or the host restarts.
        if (job.Status is "Completed" or "Failed" or "Cancelled")
        {
            return job;
        }

        try
        {
            await UpdateJobStatusFromOrchestrationAsync(job, repository, request.TenantId, cancellationToken);
        }
        catch (BackgroundJobUpdateConflictException conflict)
        {
            _logger.LogInformation(
                "Status refresh for {JobId} was superseded by {Status}; reloading authoritative metadata",
                request.JobId, conflict.CurrentStatus);
        }

        // Refresh progress as well as lifecycle fields; the first snapshot may predate an activity write.
        return await repository.GetAsync(request.JobId, request.TenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Job {request.JobId} disappeared during status refresh.");
    }

    private async Task UpdateJobStatusFromOrchestrationAsync<T>(
        BackgroundJob<T> job,
        IBackgroundJobRepository<T> repository,
        int tenantId,
        CancellationToken cancellationToken)
        where T : class, IJobDefinition
    {
        var state = await _taskHubClient.GetOrchestrationStateAsync(job.OrchestrationInstanceId ?? job.JobId);

        if (state != null)
        {
            switch (state.OrchestrationStatus)
            {
                case OrchestrationStatus.Pending:
                    return;

                case OrchestrationStatus.Running:
                    if (job.Status == "Running" && job.StartDate.HasValue)
                    {
                        return;
                    }
                    job.Status = "Running";
                    if (job.StartDate == null)
                    {
                        job.StartDate = DateTimeOffset.UtcNow;
                    }
                    break;

                case OrchestrationStatus.Completed:
                    job.Status = "Completed";
                    job.EndDate ??= state.CompletedTime == default
                        ? DateTimeOffset.UtcNow
                        : new DateTimeOffset(DateTime.SpecifyKind(state.CompletedTime, DateTimeKind.Utc));

                    // Extract output from orchestration for import jobs
                    if (job is BackgroundJob<ImportJobDefinition> && state.Output != null)
                    {
                        var output = JsonSerializer.Deserialize<ImportOrchestrationOutput>(state.Output, SerializerOptions)
                            ?? throw new JsonException("Import orchestration returned no result.");
                        job.Status = output.Status switch
                        {
                            "Completed" => "Completed",
                            "Failed" => "Failed",
                            "Cancelled" => "Cancelled",
                            _ => throw new JsonException("Import orchestration returned an invalid terminal status.")
                        };
                        job.ErrorMessage = output.ErrorMessage;
                        job.Result = JsonSerializer.SerializeToNode(new ImportJobResult
                        {
                            TotalResources = output.TotalResources,
                            TotalErrors = output.TotalErrors,
                            ErrorFileUrl = output.ErrorFileUrl,
                            CountsAreComplete = output.CountsAreComplete,
                            ResourcesWithUnknownOutcome = output.ResourcesWithUnknownOutcome
                        }, SerializerOptions);
                    }
                    else if (job is BackgroundJob<ExportJobDefinition> && state.Output != null)
                    {
                        var output = JsonSerializer.Deserialize<ExportCoordinatorOutput>(state.Output, SerializerOptions)
                            ?? throw new JsonException("Export orchestration returned no result.");
                        if (!output.Success)
                        {
                            job.Status = "Failed";
                            job.ErrorMessage = output.ErrorMessage;
                        }
                    }
                    break;

                case OrchestrationStatus.Failed:
                    job.Status = "Failed";
                    job.EndDate ??= DateTimeOffset.UtcNow;
                    job.ErrorMessage = state.FailureDetails?.ErrorMessage ?? state.Output ?? "Orchestration failed";
                    break;

                case OrchestrationStatus.Terminated:
                    job.Status = "Cancelled";
                    job.EndDate ??= DateTimeOffset.UtcNow;
                    break;

                default:
                    return;
            }

            await repository.UpdateAsync(job, tenantId, cancellationToken);
        }
    }
}
