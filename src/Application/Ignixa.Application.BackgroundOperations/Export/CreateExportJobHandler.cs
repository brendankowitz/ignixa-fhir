// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Export.Orchestrations;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Medino;

namespace Ignixa.Application.BackgroundOperations.Export;

/// <summary>
/// Handler for creating and starting FHIR bulk export jobs.
/// Validates input parameters, creates job metadata, and starts the DurableTask orchestration.
/// </summary>
public class CreateExportJobHandler : IRequestHandler<CreateExportJobCommand, CreateExportJobResult>
{
    private readonly TaskHubClient _taskHubClient;
    private readonly IBackgroundJobRepository<ExportJobDefinition> _jobRepository;

    public CreateExportJobHandler(
        TaskHubClient taskHubClient,
        IBackgroundJobRepository<ExportJobDefinition> jobRepository)
    {
        _taskHubClient = taskHubClient ?? throw new ArgumentNullException(nameof(taskHubClient));
        _jobRepository = jobRepository ?? throw new ArgumentNullException(nameof(jobRepository));
    }

    public async Task<CreateExportJobResult> HandleAsync(
        CreateExportJobCommand request,
        CancellationToken cancellationToken)
    {
        // Validate output format
        if (!string.IsNullOrEmpty(request.OutputFormat) && request.OutputFormat != "application/fhir+ndjson")
        {
            throw new ArgumentException(
                $"Unsupported output format: {request.OutputFormat}. Only 'application/fhir+ndjson' is supported.",
                nameof(request));
        }

        // Generate job ID
        var jobId = Guid.NewGuid().ToString();

        // Create job metadata
        var job = new BackgroundJob<ExportJobDefinition>
        {
            JobId = jobId,
            JobType = (int)BackgroundJobType.Export,
            Status = "Queued",
            Definition = new ExportJobDefinition
            {
                TenantId = request.TenantId,
                ResourceTypes = request.ResourceTypes,
                Since = request.Since,
                TypeFilters = request.TypeFilters,
                OutputFormat = request.OutputFormat,
                OutputPath = $"tenant/{request.TenantId}/export/{jobId}"
            },
            CreateDate = DateTimeOffset.UtcNow,
            HeartbeatDate = DateTimeOffset.UtcNow
        };

        await _jobRepository.CreateAsync(job, cancellationToken);

        // Start the orchestration
        var orchestrationInput = new ExportOrchestrationInput(
            JobId: jobId,
            TenantId: request.TenantId,
            ResourceTypes: request.ResourceTypes.ToArray(),
            Since: request.Since,
            TypeFilters: request.TypeFilters.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));

        var instance = await _taskHubClient.CreateOrchestrationInstanceAsync(
            typeof(ExportOrchestration),
            jobId, // Use jobId as instance ID for easy lookup
            orchestrationInput);

        // Update job with orchestration instance ID
        job.OrchestrationInstanceId = instance.InstanceId;
        await _jobRepository.UpdateAsync(job, request.TenantId, cancellationToken);

        return new CreateExportJobResult
        {
            JobId = jobId,
            Status = "Queued",
            OrchestrationInstanceId = instance.InstanceId,
            CreateDate = job.CreateDate
        };
    }
}
