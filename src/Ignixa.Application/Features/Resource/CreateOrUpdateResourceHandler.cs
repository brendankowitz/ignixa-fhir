// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Ignixa.Application.Features.Bundle;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Domain;
using Ignixa.Specification;
using Ignixa.Search.Indexing;
using Ignixa.Serialization;

namespace Ignixa.Application.Features.Resource;

/// <summary>
/// Generic handler for creating or updating any FHIR resource.
/// Multi-tenant enabled: Uses IPartitionStrategy + IFhirRepositoryFactory.
/// Supports both immediate writes (standalone operations) and deferred writes (bundle operations).
/// Coordinator can be passed via command parameter OR via HttpContext.Items (pipeline routing).
///
/// Flow:
/// 1. Validate resource using tenant-configured validation tier
/// 2. Determine partition using IPartitionStrategy (for write)
/// 3. Validate single partition (writes always go to one partition)
/// 4. Get repository from IFhirRepositoryFactory
/// 5. Execute CreateOrUpdateAsync directly (no execution strategy for CRUD)
/// </summary>
public class CreateOrUpdateResourceHandler : IRequestHandler<CreateOrUpdateResourceCommand, UpdateResult>
{
    private readonly IPartitionStrategy _partitionStrategy;
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IFhirVersionContext _fhirVersionContext;
    private readonly ILogger<CreateOrUpdateResourceHandler> _logger;

    public CreateOrUpdateResourceHandler(
        IPartitionStrategy partitionStrategy,
        IFhirRepositoryFactory repositoryFactory,
        IHttpContextAccessor httpContextAccessor,
        IFhirVersionContext fhirVersionContext,
        ILogger<CreateOrUpdateResourceHandler> logger)
    {
        _partitionStrategy = partitionStrategy ?? throw new ArgumentNullException(nameof(partitionStrategy));
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _fhirVersionContext = fhirVersionContext ?? throw new ArgumentNullException(nameof(fhirVersionContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<UpdateResult> HandleAsync(CreateOrUpdateResourceCommand command, CancellationToken cancellationToken)
    {
        // Business logic - always runs for both bundle and standalone operations
        // NOTE: Validation now handled by ValidationBehavior in the pipeline
        _logger.LogInformation(
            "Processing CreateOrUpdateResource for {ResourceType}/{Id}",
            command.ResourceType,
            command.Id);

        // Extract FHIR version from headers (defaults to R4)
        var fhirVersionEnum = FhirVersionExtractor.ExtractFhirVersion(_httpContextAccessor.HttpContext);
        var schemaProvider = _fhirVersionContext.GetSchemaProvider(fhirVersionEnum);

        // Create wrapper (needed for both paths now)
        var wrapper = CreateResourceWrapper(command, fhirVersionEnum, schemaProvider);

        UpdateResult result;

        // Resolve coordinator from command OR HttpContext.Items (pipeline routing fallback)
        DeferredWriteCoordinator? coordinator = command.Coordinator
            ?? _httpContextAccessor.HttpContext.GetDeferredWriteCoordinator();

        if (coordinator != null && command.Coordinator == null)
        {
            _logger.LogDebug(
                "Resolved DeferredWriteCoordinator from HttpContext.Items for {ResourceType}/{Id}",
                command.ResourceType,
                command.Id);
        }

        // Routing logic - coordinator presence determines path
        if (coordinator != null)
        {
            // Bundle path - queue for deferred batch write
            _logger.LogDebug(
                "Using deferred write coordinator for {ResourceType}/{Id}",
                command.ResourceType,
                command.Id);

            // Get entry index from HttpContext.Items if available
            int entryIndex = _httpContextAccessor.HttpContext.GetBundleEntryIndex();

            _logger.LogWarning(
                "HANDLER: Retrieved entry index {EntryIndex} from HttpContext for {ResourceType}/{ResourceId}",
                entryIndex,
                command.ResourceType,
                command.Id);

            // Queue wrapper for deferred batch write
            var key = await coordinator.QueueWriteAsync(
                wrapper,
                entryIndex,
                cancellationToken);

            // For bundle operations, construct minimal UpdateResult from ResourceKey
            // Full resource not available until batch is committed
            var resourceJson = System.Text.Json.JsonSerializer.Serialize(command.JsonNode.MutableNode);
            result = new UpdateResult(
                Key: key,
                ResourceBytes: System.Text.Encoding.UTF8.GetBytes(resourceJson),
                LastModified: DateTimeOffset.UtcNow);
        }
        else
        {
            // Standalone path - write immediately to repository via multi-tenant factory
            // 1. Extract tenant context from HttpContext.Items
            var httpContext = _httpContextAccessor.HttpContext
                ?? throw new InvalidOperationException("HttpContext is null");

            if (!httpContext.Items.TryGetValue("TenantId", out var tenantIdObj) || tenantIdObj is not int tenantId)
            {
                throw new InvalidOperationException("TenantId not found in HttpContext.Items");
            }

            var tenantConfig = httpContext.Items["TenantConfiguration"] as TenantConfiguration;

            // Create partition resolution context
            var partitionContext = new PartitionResolutionContext
            {
                TenantId = tenantId,
                TenantConfiguration = tenantConfig
            };

            // 2. Determine partition using IPartitionStrategy
            var partition = _partitionStrategy.DetermineWritePartition(
                partitionContext,
                command.JsonNode);

            // 3. Validate single partition (writes always go to one partition)
            if (partition.PartitionIds.Count != 1)
            {
                _logger.LogError(
                    "Write operation requires exactly 1 partition, received {Count} partition IDs",
                    partition.PartitionIds.Count);
                throw new InvalidOperationException(
                    $"Write operation requires exactly 1 partition, received {partition.PartitionIds.Count} partition IDs");
            }

            int resolvedTenantId = partition.PartitionIds[0];

            _logger.LogDebug(
                "Partition determined for write: {TenantId} (Mode: {Mode})",
                resolvedTenantId,
                partition.Mode);

            // 4. Get repository from factory
            var repository = await _repositoryFactory.GetRepositoryAsync(resolvedTenantId, cancellationToken);

            // 5. Write immediately to repository - returns UpdateResult with ResourceKey + raw bytes
            result = await repository.CreateOrUpdateAsync(wrapper, cancellationToken);
        }

        // Success logging - always runs for both bundle and standalone operations
        _logger.LogInformation(
            "Created/Updated {ResourceType}/{Id} with version {VersionId}",
            result.Key.ResourceType,
            result.Key.Id,
            result.Key.VersionId);

        return result;
    }

    /// <summary>
    /// Creates a ResourceWrapper from the command.
    /// Single place for wrapper construction logic.
    /// Uses provided FHIR version and schema provider to extract search indices from resource.
    /// </summary>
    private ResourceWrapper CreateResourceWrapper(
        CreateOrUpdateResourceCommand command,
        FhirSpecification fhirVersionEnum,
        IFhirSchemaProvider schemaProvider)
    {
        var request = new ResourceRequest(command.HttpMethod.Method, $"{command.ResourceType}/{command.Id}");

        // Get version-specific search indexer from context
        // Factory initializes synchronously using pre-generated search parameters
        var searchIndexer = _fhirVersionContext.GetSearchIndexer(fhirVersionEnum);

        // Extract search indices using version-specific indexer
        IReadOnlyCollection<SearchIndexEntry>? searchIndices = null;
        try
        {
            var typedElement = command.JsonNode.ToTypedElement(schemaProvider);
            searchIndices = searchIndexer.Extract(typedElement);

            _logger.LogDebug(
                "Extracted {Count} search indices for {ResourceType}/{Id} (FHIR {Version})",
                searchIndices.Count,
                command.ResourceType,
                command.Id,
                fhirVersionEnum);
        }
        catch (Exception ex)
        {
            // Log but don't fail - search indexing is optional for now
            _logger.LogWarning(
                ex,
                "Failed to extract search indices for {ResourceType}/{Id} (FHIR {Version})",
                command.ResourceType,
                command.Id,
                fhirVersionEnum);
        }

        // Set initial meta values
        // For POST: Always version 1 (new resource)
        // For PUT: Start with version 1, repository will calculate actual version for updates
        command.JsonNode.Meta.LastUpdated = DateTimeOffset.UtcNow;
        command.JsonNode.Meta.VersionId = "1"; // Repository will update this for existing resources

        if (command.HttpMethod == HttpMethod.Post)
        {
            command.JsonNode.Id = command.Id;
        }

        return new ResourceWrapper(
            command.ResourceType,
            command.Id,
            command.JsonNode.Meta.VersionId, // Version will be determined by repository
            command.JsonNode.Meta.LastUpdated.Value,
            command.JsonNode, // Pass ResourceJsonNode directly (data layer serializes as needed)
            request,
            false) // isDeleted
        {
            FhirVersion = fhirVersionEnum.ToVersionString(), // Convert enum to string for storage
            SearchIndices = searchIndices?.ToArray()
        };
    }
}
