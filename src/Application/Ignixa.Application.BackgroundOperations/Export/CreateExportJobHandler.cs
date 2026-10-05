// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Export.Models;
using Ignixa.Application.BackgroundOperations.Export.Orchestrations;
using Ignixa.Application.Features.Search;
using Ignixa.DataLayer.BlobStorage;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Ignixa.SqlOnFhir.Parsing;
using Ignixa.Specification.ValueSets.Normative;
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
    private readonly ITenantConfigurationStore _tenantConfigurationStore;
    private readonly IFhirVersionContext _fhirVersionContext;
    private readonly ExportGroupResolver _groupResolver;

    public CreateExportJobHandler(
        TaskHubClient taskHubClient,
        IBackgroundJobRepository<ExportJobDefinition> jobRepository,
        ITenantConfigurationStore tenantConfigurationStore,
        IFhirVersionContext fhirVersionContext,
        ExportGroupResolver groupResolver,
        ViewDefinitionLoader? viewDefinitionLoader = null)
    {
        _taskHubClient = taskHubClient ?? throw new ArgumentNullException(nameof(taskHubClient));
        _jobRepository = jobRepository ?? throw new ArgumentNullException(nameof(jobRepository));
        _viewDefinitionLoader = viewDefinitionLoader;
        _tenantConfigurationStore = tenantConfigurationStore ?? throw new ArgumentNullException(nameof(tenantConfigurationStore));
        _fhirVersionContext = fhirVersionContext ?? throw new ArgumentNullException(nameof(fhirVersionContext));
        _groupResolver = groupResolver ?? throw new ArgumentNullException(nameof(groupResolver));
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

        var tenant = await _tenantConfigurationStore.GetTenantConfigurationAsync(request.TenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {request.TenantId} not found or inactive");
        var version = FhirSpecificationExtensions.FromVersionString(tenant.FhirVersion);
        var schema = _fhirVersionContext.GetSchemaProvider(version, request.TenantId);

        if (!string.IsNullOrEmpty(request.GroupId))
        {
            // Validate even when there are no resource ranges and no worker will run.
            await _groupResolver.ResolvePatientIdsAsync(request.TenantId, request.GroupId, schema, cancellationToken);
        }

        if (request.ResourceTypes.Count == 0)
        {
            IEnumerable<string> resourceTypes = schema.ResourceTypeNames
                .Where(type => !(schema.GetTypeDefinition(type)
                    ?? throw new InvalidOperationException($"Tenant schema declares '{type}' without a type definition.")).Info.IsAbstract);
            if (!string.IsNullOrEmpty(request.GroupId))
            {
                var compartments = _fhirVersionContext.GetCompartmentDefinitionManager(version);
                resourceTypes = resourceTypes.Where(type => type == "Patient" ||
                    (compartments.TryGetSearchParams(type, CompartmentType.Patient, out var parameters) &&
                        parameters.Count > 0));
            }

            // Persist discovery in new input; the orchestrator's empty-list fallback belongs to old histories.
            var snapshot = resourceTypes.Order(StringComparer.Ordinal).ToArray();
            if (snapshot.Length == 0)
            {
                throw new InvalidOperationException("Tenant schema has no applicable export resource types.");
            }
            request = request with { ResourceTypes = snapshot };
        }

        // Generate job ID
        var jobId = Guid.NewGuid().ToString();

        // Create job metadata
        var job = new BackgroundJob<ExportJobDefinition>
        {
            JobId = jobId,
            OrchestrationInstanceId = jobId,
            JobType = (int)BackgroundJobType.Export,
            Status = "Queued",
            Definition = new ExportJobDefinition
            {
                TenantId = request.TenantId,
                ResourceTypes = request.ResourceTypes,
                Since = request.Since,
                TypeFilters = request.TypeFilters,
                OutputFormat = request.OutputFormat,
                OutputPath = $"partition/{request.TenantId}/export/{jobId}",
                GroupId = request.GroupId,
                RequestUrl = request.RequestUrl
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

        return new CreateExportJobResult
        {
            JobId = jobId,
            Status = "Queued",
            OrchestrationInstanceId = instance.InstanceId,
            CreateDate = job.CreateDate
        };
    }
}
