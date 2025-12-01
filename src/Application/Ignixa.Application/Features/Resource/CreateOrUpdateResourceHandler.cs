// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Medino;
using Microsoft.Extensions.Logging;
using Ignixa.Application.Features.Bundle;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Domain;
using Ignixa.Specification;
using Ignixa.Search.Indexing;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using System.Text.Json.Nodes;
using Ignixa.Application.Features.Search;

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
    private readonly Func<FhirVersion, IValidationSchemaResolver> _schemaResolverFactory;
    private readonly ILogger<CreateOrUpdateResourceHandler> _logger;

    public CreateOrUpdateResourceHandler(
        IPartitionStrategy partitionStrategy,
        IFhirRepositoryFactory repositoryFactory,
        IFhirRequestContextAccessor contextAccessor,
        IFhirVersionContext fhirVersionContext,
        Func<FhirVersion, IValidationSchemaResolver> schemaResolverFactory,
        ILogger<CreateOrUpdateResourceHandler> logger)
    {
        _partitionStrategy = partitionStrategy ?? throw new ArgumentNullException(nameof(partitionStrategy));
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _fhirVersionContext = fhirVersionContext ?? throw new ArgumentNullException(nameof(fhirVersionContext));
        _schemaResolverFactory = schemaResolverFactory ?? throw new ArgumentNullException(nameof(schemaResolverFactory));
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
        FhirVersion fhirVersionEnum,
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
            var typedElement = command.JsonNode.ToElement(schemaProvider);
            searchIndices = searchIndexer.Extract((IElement)typedElement);

            _logger.LogDebug(
                "Extracted {Count} search indices for {ResourceType}/{Id} (FHIR {Version})",
                searchIndices.Count,
                command.ResourceType,
                command.Id,
                fhirVersionEnum);
        }
        catch (Exception ex)
        {
            // Log with full details to help diagnose indexing failures
            _logger.LogError(
                ex,
                "Failed to extract search indices for {ResourceType}/{Id} (FHIR {Version}). Error: {ErrorMessage}. This may indicate a bug in search parameter extraction for this resource type.",
                command.ResourceType,
                command.Id,
                fhirVersionEnum,
                ex.Message);

            // Re-throw to fail the request - search indexing is not optional
            throw new InvalidOperationException(
                $"Failed to extract search indices for {command.ResourceType}/{command.Id}: {ex.Message}. See logs for details.",
                ex);
        }

        // Set initial meta values
        // For POST: Always version 1 (new resource)
        // For PUT: Start with version 1, repository will calculate actual version for updates
        command.JsonNode.Meta.LastUpdated = DateTimeOffset.UtcNow;
        command.JsonNode.Meta.VersionId = "1"; // Repository will update this for existing resources

        // Always set the ID on the JsonNode to match command.Id
        // This is critical for:
        // - POST: Server-assigned ID
        // - PUT with conditional update: Existing resource ID (body ID ignored)
        // - PUT with explicit ID: URL ID takes precedence
        command.JsonNode.Id = command.Id;

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
        ProvenanceJsonNode provenanceTemplate,
        UpdateResult mainResourceResult,
        FhirVersion fhirVersion,
        IFhirSchemaProvider schemaProvider,
        int? tenantId,
        IFhirRepository repository,
        CancellationToken cancellationToken)
    {
        // Generate ID for the Provenance resource (using same strategy as main resource creation)
        var provenanceId = Guid.NewGuid().ToString();

        // Set ID on the provenance resource
        provenanceTemplate.Id = provenanceId;

        // Add target reference with version-specific reference to the created/updated resource
        // Uses type-safe AddTarget method from ProvenanceJsonNode
        provenanceTemplate.AddTarget(
            mainResourceResult.Key.ResourceType,
            mainResourceResult.Key.Id,
            mainResourceResult.Key.VersionId!);

        _logger.LogDebug(
            "Created Provenance resource {ProvenanceId} with target reference: {TargetType}/{TargetId}/_history/{TargetVersion}",
            provenanceId,
            mainResourceResult.Key.ResourceType,
            mainResourceResult.Key.Id,
            mainResourceResult.Key.VersionId);

        // Set meta values
        provenanceTemplate.Meta.LastUpdated = DateTimeOffset.UtcNow;
        provenanceTemplate.Meta.VersionId = "1";

        // Validate the Provenance resource before persisting
        // This ensures X-Provenance resources go through the same validation as normal resources
        ValidateProvenance(provenanceTemplate, fhirVersion);

        // Create ResourceWrapper for the Provenance resource
        var request = new ResourceRequest("POST", $"Provenance/{provenanceId}");

        // Extract search indices for Provenance
        var searchIndexer = _fhirVersionContext.GetSearchIndexer(fhirVersion, tenantId);
        IReadOnlyCollection<SearchIndexEntry>? searchIndices = null;
        try
        {
            var typedElement = provenanceTemplate.ToElement(schemaProvider);
            searchIndices = searchIndexer.Extract((IElement)typedElement);

            _logger.LogDebug(
                "Extracted {Count} search indices for Provenance/{Id}",
                searchIndices.Count,
                provenanceId);
        }
        catch (Exception ex)
        {
            // Log with full details to help diagnose indexing failures
            _logger.LogError(
                ex,
                "Failed to extract search indices for Provenance/{Id}. Error: {ErrorMessage}. This may indicate a bug in search parameter extraction.",
                provenanceId,
                ex.Message);

            // Re-throw to fail the request - search indexing is not optional
            throw new InvalidOperationException(
                $"Failed to extract search indices for Provenance/{provenanceId}: {ex.Message}. See logs for details.",
                ex);
        }

        var provenanceWrapper = new ResourceWrapper(
            "Provenance",
            provenanceId,
            provenanceTemplate.Meta.VersionId!,
            provenanceTemplate.Meta.LastUpdated!.Value,
            provenanceTemplate, // ProvenanceJsonNode extends ResourceJsonNode, so this works
            request,
            false) // isDeleted
        {
            FhirVersion = fhirVersion.ToVersionString(),
            SearchIndices = searchIndices?.ToArray()
        };

        // Persist the Provenance resource (validation was performed above by ValidateProvenance)
        var provenanceResult = await repository.CreateOrUpdateAsync(provenanceWrapper, cancellationToken);

        _logger.LogInformation(
            "Created Provenance resource {ProvenanceId} (version {VersionId}) for {TargetType}/{TargetId}",
            provenanceResult.Key.Id,
            provenanceResult.Key.VersionId,
            mainResourceResult.Key.ResourceType,
            mainResourceResult.Key.Id);
    }

    /// <summary>
    /// Validates a Provenance resource using the same validation logic as the ValidationBehavior pipeline.
    /// Ensures X-Provenance header resources are validated before persistence.
    /// </summary>
    /// <param name="provenance">The Provenance resource to validate.</param>
    /// <param name="fhirVersion">The FHIR version to use for validation.</param>
    /// <exception cref="ValidationException">Thrown when validation fails.</exception>
    private void ValidateProvenance(ProvenanceJsonNode provenance, FhirVersion fhirVersion)
    {
        _logger.LogDebug("Validating Provenance resource from X-Provenance header");

        var schemaResolver = _schemaResolverFactory(fhirVersion);
        var schema = schemaResolver.GetSchema("Provenance");

        if (schema == null)
        {
            _logger.LogWarning("No validation schema found for Provenance resource type");
            return;
        }

        var schemaProvider = _fhirVersionContext.GetBaseSchemaProvider(fhirVersion);
        var element = provenance.ToElement(schemaProvider);
        var settings = new ValidationSettings
        {
            Depth = ValidationDepth.Spec // Use Spec-level validation for X-Provenance
        };
        var state = new ValidationState();
        var validationResult = schema.Validate(element, settings, state);

        if (!validationResult.IsValid)
        {
            _logger.LogWarning(
                "X-Provenance validation failed: {ErrorCount} error(s), {WarningCount} warning(s)",
                validationResult.Issues.Count(i => i.Severity == IssueSeverity.Error || i.Severity == IssueSeverity.Fatal),
                validationResult.Issues.Count(i => i.Severity == IssueSeverity.Warning));

            throw new ValidationException(validationResult);
        }

        _logger.LogDebug("X-Provenance validation passed");
    }
}
