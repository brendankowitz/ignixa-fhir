// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.Extensions.Logging;
using Ignixa.Application.Features.Bundle;
using Ignixa.Application.Infrastructure;
using Ignixa.Search.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Domain;
using Ignixa.Specification;
using Ignixa.Search.Indexing;
using Ignixa.Serialization;
using System.Text.Json.Nodes;

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
    private readonly IFhirRequestContextAccessor _contextAccessor;
    private readonly IFhirVersionContext _fhirVersionContext;
    private readonly ILogger<CreateOrUpdateResourceHandler> _logger;

    public CreateOrUpdateResourceHandler(
        IPartitionStrategy partitionStrategy,
        IFhirRepositoryFactory repositoryFactory,
        IFhirRequestContextAccessor contextAccessor,
        IFhirVersionContext fhirVersionContext,
        ILogger<CreateOrUpdateResourceHandler> logger)
    {
        _partitionStrategy = partitionStrategy ?? throw new ArgumentNullException(nameof(partitionStrategy));
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _fhirVersionContext = fhirVersionContext ?? throw new ArgumentNullException(nameof(fhirVersionContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<UpdateResult> HandleAsync(CreateOrUpdateResourceCommand command, CancellationToken cancellationToken)
    {
        // Get FHIR request context (populated by FhirRequestContextMiddleware)
        var context = _contextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available");

        // Business logic - always runs for both bundle and standalone operations
        // NOTE: Validation now handled by ValidationBehavior in the pipeline
        _logger.LogInformation(
            "Processing CreateOrUpdateResource for {ResourceType}/{Id}",
            command.ResourceType,
            command.Id);

        // Use FHIR version from context
        var fhirVersionEnum = context.FhirVersion;
        var schemaProvider = _fhirVersionContext.GetBaseSchemaProvider(fhirVersionEnum);

        // Tenant ID available from context for tenant-aware search indexing
        int? tenantId = context.TenantId;

        // Create wrapper (needed for both paths now)
        var wrapper = CreateResourceWrapper(command, fhirVersionEnum, schemaProvider, tenantId);

        UpdateResult result;

        // Resolve coordinator from command OR context (pipeline routing fallback)
        DeferredWriteCoordinator? coordinator = command.Coordinator
            ?? context.DeferredWriteCoordinator;

        if (coordinator != null && command.Coordinator == null)
        {
            _logger.LogDebug(
                "Resolved DeferredWriteCoordinator from FHIR request context for {ResourceType}/{Id}",
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

            // Get entry index from context if available
            int entryIndex = context.BundleEntryIndex ?? 0;

            _logger.LogWarning(
                "HANDLER: Retrieved entry index {EntryIndex} from context for {ResourceType}/{ResourceId}",
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
            // 1. Validate tenant context (already available from context)
            if (!tenantId.HasValue)
            {
                throw new InvalidOperationException("TenantId not available in FHIR request context");
            }

            // Create partition resolution context from FHIR request context
            var partitionContext = new PartitionResolutionContext
            {
                TenantId = tenantId.Value,
                TenantConfiguration = context.TenantConfiguration
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

            // 6. Process X-Provenance header if provided (only for standalone operations)
            // Provenance cannot be processed in bundle/deferred context because the main resource isn't persisted yet
            if (command.ProvenanceResource != null)
            {
                _logger.LogInformation(
                    "Processing X-Provenance header for {ResourceType}/{Id}",
                    result.Key.ResourceType,
                    result.Key.Id);

                await ProcessProvenanceAsync(
                    command.ProvenanceResource,
                    result,
                    fhirVersionEnum,
                    schemaProvider,
                    tenantId,
                    repository,
                    cancellationToken);
            }
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
    /// When tenantId is provided, uses tenant-aware search indexer with IG-specific search parameters.
    /// </summary>
    private ResourceWrapper CreateResourceWrapper(
        CreateOrUpdateResourceCommand command,
        FhirSpecification fhirVersionEnum,
        IFhirSchemaProvider schemaProvider,
        int? tenantId)
    {
        var request = new ResourceRequest(command.HttpMethod.Method, $"{command.ResourceType}/{command.Id}");

        // Get tenant-aware search indexer from context if tenantId available
        // When tenantId is provided, indexer uses IG-specific search parameters from loaded packages
        // When no tenantId, indexer uses base FHIR spec search parameters only
        var searchIndexer = _fhirVersionContext.GetSearchIndexer(fhirVersionEnum, tenantId);

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

    /// <summary>
    /// Processes the Provenance resource from X-Provenance header.
    /// Fills in the target reference to point to the created/updated resource and persists the Provenance.
    /// </summary>
    /// <param name="provenanceTemplate">The Provenance resource from X-Provenance header (without target).</param>
    /// <param name="mainResourceResult">The result from creating/updating the main resource.</param>
    /// <param name="fhirVersion">The FHIR version being used.</param>
    /// <param name="schemaProvider">The schema provider for the FHIR version.</param>
    /// <param name="tenantId">The tenant ID for search indexing.</param>
    /// <param name="repository">The repository to persist the Provenance resource.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task ProcessProvenanceAsync(
        Serialization.SourceNodes.ResourceJsonNode provenanceTemplate,
        UpdateResult mainResourceResult,
        FhirSpecification fhirVersion,
        IFhirSchemaProvider schemaProvider,
        int? tenantId,
        IFhirRepository repository,
        CancellationToken cancellationToken)
    {
        // Clone the provenance node and add target reference
        var provenanceJson = provenanceTemplate.SerializeToString();
        var provenanceObject = JsonNode.Parse(provenanceJson)?.AsObject()
            ?? throw new InvalidOperationException("Failed to parse Provenance resource from X-Provenance header");

        // Generate ID for the Provenance resource (using same strategy as main resource creation)
        var provenanceId = Guid.NewGuid().ToString();

        // Set ID on the provenance resource
        provenanceObject["id"] = provenanceId;

        // Add target reference array with version-specific reference to the created/updated resource
        var targetReference = $"{mainResourceResult.Key.ResourceType}/{mainResourceResult.Key.Id}/_history/{mainResourceResult.Key.VersionId}";
        var targetArray = new JsonArray
        {
            new JsonObject
            {
                ["reference"] = targetReference
            }
        };
        provenanceObject["target"] = targetArray;

        _logger.LogDebug(
            "Created Provenance resource {ProvenanceId} with target reference: {TargetReference}",
            provenanceId,
            targetReference);

        // Parse back to ResourceJsonNode
        var updatedProvenanceJson = provenanceObject.ToJsonString();
        Serialization.SourceNodes.ResourceJsonNode provenanceNode;
        using (var memoryStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(updatedProvenanceJson)))
        {
            provenanceNode = await Serialization.SourceNodes.JsonSourceNodeFactory.Parse(memoryStream);
        }

        // Set meta values
        provenanceNode.Meta.LastUpdated = DateTimeOffset.UtcNow;
        provenanceNode.Meta.VersionId = "1";
        provenanceNode.Id = provenanceId;

        // Create ResourceWrapper for the Provenance resource
        var request = new ResourceRequest("POST", $"Provenance/{provenanceId}");

        // Extract search indices for Provenance
        var searchIndexer = _fhirVersionContext.GetSearchIndexer(fhirVersion, tenantId);
        IReadOnlyCollection<SearchIndexEntry>? searchIndices = null;
        try
        {
            var typedElement = provenanceNode.ToTypedElement(schemaProvider);
            searchIndices = searchIndexer.Extract(typedElement);

            _logger.LogDebug(
                "Extracted {Count} search indices for Provenance/{Id}",
                searchIndices.Count,
                provenanceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to extract search indices for Provenance/{Id}",
                provenanceId);
        }

        var provenanceWrapper = new ResourceWrapper(
            "Provenance",
            provenanceId,
            provenanceNode.Meta.VersionId,
            provenanceNode.Meta.LastUpdated.Value,
            provenanceNode,
            request,
            false) // isDeleted
        {
            FhirVersion = fhirVersion.ToVersionString(),
            SearchIndices = searchIndices?.ToArray()
        };

        // Persist the Provenance resource
        // Note: Validation will be performed by ValidationBehavior for the Provenance resource
        // However, since we're calling repository directly, validation behavior won't run
        // The Provenance resource has already been parsed, so basic JSON validation is done
        // FHIR-level validation (required fields, cardinality, etc.) is delegated to the repository or skipped
        var provenanceResult = await repository.CreateOrUpdateAsync(provenanceWrapper, cancellationToken);

        _logger.LogInformation(
            "Created Provenance resource {ProvenanceId} (version {VersionId}) for {TargetType}/{TargetId}",
            provenanceResult.Key.Id,
            provenanceResult.Key.VersionId,
            mainResourceResult.Key.ResourceType,
            mainResourceResult.Key.Id);
    }
}
