// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Export.Models;
using Ignixa.Application.BackgroundOperations.Export.Orchestrations;
using Ignixa.DataLayer.BlobStorage;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.SqlOnFhir.Parsing;
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
    private readonly ViewDefinitionLoader? _viewDefinitionLoader;

    public CreateExportJobHandler(
        TaskHubClient taskHubClient,
        IBackgroundJobRepository<ExportJobDefinition> jobRepository,
        ViewDefinitionLoader? viewDefinitionLoader = null)
    {
        _taskHubClient = taskHubClient ?? throw new ArgumentNullException(nameof(taskHubClient));
        _jobRepository = jobRepository ?? throw new ArgumentNullException(nameof(jobRepository));
        _viewDefinitionLoader = viewDefinitionLoader;
    }

    public async Task<CreateExportJobResult> HandleAsync(
        CreateExportJobCommand request,
        CancellationToken cancellationToken)
    {
        // Validate output format
        if (!string.IsNullOrEmpty(request.OutputFormat) &&
            request.OutputFormat != ExportConstants.MediaTypeNdjson &&
            request.OutputFormat != ExportConstants.MediaTypeParquet)
        {
            throw new ArgumentException(
                $"Unsupported output format: {request.OutputFormat}. Supported formats: {ExportConstants.MediaTypeNdjson}, {ExportConstants.MediaTypeParquet}",
                nameof(request));
        }

        // Validate and normalize ViewDefinition resource type
        if (!string.IsNullOrEmpty(request.ViewDefinitionId) && _viewDefinitionLoader != null)
        {
            try
            {
                var viewDefinitionNode = await _viewDefinitionLoader.LoadViewDefinitionAsync(
                    request.TenantId,
                    request.ViewDefinitionId,
                    cancellationToken);

                var viewExpression = ViewDefinitionExpressionParser.Parse(viewDefinitionNode);
                var viewResourceType = viewExpression.Resource;

                if (request.ResourceTypes.Any())
                {
                    // If resource types were explicitly requested, ViewDefinition must match one of them
                    if (!request.ResourceTypes.Contains(viewResourceType, StringComparer.OrdinalIgnoreCase))
                    {
                        throw new BadRequestException(
                            $"ViewDefinition '{request.ViewDefinitionId}' targets resource type '{viewResourceType}', but export requests: {string.Join(", ", request.ResourceTypes)}. ViewDefinition resource type must be included in export request.");
                    }
                }
                else
                {
                    // If no resource types specified, automatically use ViewDefinition's target resource type
                    request = request with { ResourceTypes = new[] { viewResourceType } };
                }
            }
            catch (BadRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new BadRequestException(
                    $"Failed to load or validate ViewDefinition '{request.ViewDefinitionId}': {ex.Message}", ex);
            }
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
                OutputPath = $"tenant/{request.TenantId}/export/{jobId}",
                GroupId = request.GroupId
            },
            CreateDate = DateTimeOffset.UtcNow,
            HeartbeatDate = DateTimeOffset.UtcNow
        };

        await _jobRepository.CreateAsync(job, cancellationToken);

        // Start the orchestration
        var orchestrationInput = new ExportCoordinatorInput(
            JobId: jobId,
            TenantId: request.TenantId,
            ResourceTypes: request.ResourceTypes.ToArray(),
            Since: request.Since,
            TypeFilters: request.TypeFilters.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            NumberOfRangesPerType: 6, // Default parallelism: 6 ranges per type
            OutputFormat: request.OutputFormat ?? ExportConstants.MediaTypeNdjson,
            ViewDefinitionId: request.ViewDefinitionId,
            GroupId: request.GroupId);

        var instance = await _taskHubClient.CreateOrchestrationInstanceAsync(
            typeof(ExportOrchestration),
            jobId, // Use jobId as instance ID for easy lookup
            orchestrationInput);

        // Update job with orchestration instance ID and mark when processing started
        job.OrchestrationInstanceId = instance.InstanceId;
        job.StartDate = DateTimeOffset.UtcNow;
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
