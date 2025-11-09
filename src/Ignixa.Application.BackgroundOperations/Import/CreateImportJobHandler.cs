// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Import.Models;
using Ignixa.Application.BackgroundOperations.Import.Orchestrations;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Medino;

namespace Ignixa.Application.BackgroundOperations.Import;

/// <summary>
/// Handler for creating and starting FHIR bulk import jobs.
/// Validates input parameters, creates job metadata, and starts the DurableTask orchestration.
/// </summary>
public class CreateImportJobHandler : IRequestHandler<CreateImportJobCommand, CreateImportJobResult>
{
    private readonly TaskHubClient _taskHubClient;
    private readonly IBackgroundJobRepository<ImportJobDefinition> _jobRepository;

    public CreateImportJobHandler(
        TaskHubClient taskHubClient,
        IBackgroundJobRepository<ImportJobDefinition> jobRepository)
    {
        _taskHubClient = taskHubClient ?? throw new ArgumentNullException(nameof(taskHubClient));
        _jobRepository = jobRepository ?? throw new ArgumentNullException(nameof(jobRepository));
    }

    public async Task<CreateImportJobResult> HandleAsync(
        CreateImportJobCommand request,
        CancellationToken cancellationToken)
    {
        // Validate mode
        if (request.Mode != "InitialLoad" && request.Mode != "IncrementalLoad")
        {
            throw new ArgumentException(
                $"Invalid mode '{request.Mode}'. Must be 'InitialLoad' or 'IncrementalLoad'.",
                nameof(request));
        }

        // Validate input files
        if (request.InputFiles == null || request.InputFiles.Count == 0)
        {
            throw new ArgumentException("At least one input file must be specified.", nameof(request));
        }

        foreach (var file in request.InputFiles)
        {
            if (string.IsNullOrWhiteSpace(file.Type) || string.IsNullOrWhiteSpace(file.Url))
            {
                throw new ArgumentException(
                    "Each input file must have 'type' and 'url' properties.",
                    nameof(request));
            }
        }

        // Validate configuration values
        var batchSize = request.BatchSize < 1 ? 1 : request.BatchSize;
        var channelCapacity = request.ChannelCapacity < 1 ? 1 : request.ChannelCapacity;

        // Generate job ID
        var jobId = Guid.NewGuid().ToString("N");

        // Create job metadata
        var job = new BackgroundJob<ImportJobDefinition>
        {
            JobId = jobId,
            JobType = (int)BackgroundJobType.Import,
            Status = "Queued",
            Definition = new ImportJobDefinition
            {
                TenantId = request.TenantId,
                InputFormat = "application/fhir+ndjson",
                InputSource = string.Join(", ", request.InputFiles.Select(f => f.Type)),
                Mode = request.Mode,
                InputFiles = request.InputFiles
            },
            CreateDate = DateTimeOffset.UtcNow,
            HeartbeatDate = DateTimeOffset.UtcNow
        };

        await _jobRepository.CreateAsync(job, cancellationToken);

        // Start the orchestration
        var orchestrationInput = new ImportOrchestrationInput
        {
            JobId = jobId,
            TenantId = request.TenantId,
            InputFiles = request.InputFiles,
            Mode = request.Mode,
            StorageDetail = request.StorageDetail,
            BatchSize = batchSize,
            ChannelCapacity = channelCapacity
        };

        var instance = await _taskHubClient.CreateOrchestrationInstanceAsync(
            typeof(ImportOrchestration),
            jobId, // Use jobId as instance ID for easy lookup
            orchestrationInput);

        // Update job with orchestration instance ID
        job.OrchestrationInstanceId = instance.InstanceId;
        await _jobRepository.UpdateAsync(job, request.TenantId, cancellationToken);

        return new CreateImportJobResult
        {
            JobId = jobId,
            Status = "Queued",
            OrchestrationInstanceId = instance.InstanceId,
            CreateDate = job.CreateDate
        };
    }
}
