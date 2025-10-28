using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ignixa.Application.Features.Patch.Validation;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Patch;

/// <summary>
/// Handles PATCH operations on FHIR resources.
/// </summary>
public class PatchResourceHandler : IRequestHandler<PatchResourceCommand, ResourceWrapper?>
{
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly FhirPatchParametersParser _parametersParser;
    private readonly FhirPatchEngine _patchEngine;
    private readonly FhirPatchValidator _fhirPatchValidator;
    private readonly ImmutablePropertyValidator _immutablePropertyValidator;
    private readonly ILogger<PatchResourceHandler> _logger;

    public PatchResourceHandler(
        IFhirRepositoryFactory repositoryFactory,
        FhirPatchParametersParser parametersParser,
        FhirPatchEngine patchEngine,
        FhirPatchValidator fhirPatchValidator,
        ImmutablePropertyValidator immutablePropertyValidator,
        ILogger<PatchResourceHandler> logger)
    {
        _repositoryFactory = repositoryFactory;
        _parametersParser = parametersParser;
        _patchEngine = patchEngine;
        _fhirPatchValidator = fhirPatchValidator;
        _immutablePropertyValidator = immutablePropertyValidator;
        _logger = logger;
    }

    public async Task<ResourceWrapper?> HandleAsync(
        PatchResourceCommand request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "PATCH request: {ResourceType}/{ResourceId} in tenant {TenantId}",
            request.ResourceType,
            request.ResourceId,
            request.TenantId);

        // 1. Get tenant-specific repository
        var repository = await _repositoryFactory.GetRepositoryAsync(request.TenantId, cancellationToken);

        // 2. Fetch existing resource
        var key = new ResourceKey(request.ResourceType, request.ResourceId);
        var existing = await repository.GetAsync(key, cancellationToken);

        if (existing == null)
        {
            _logger.LogWarning(
                "PATCH failed: Resource {ResourceType}/{ResourceId} not found in tenant {TenantId}",
                request.ResourceType,
                request.ResourceId,
                request.TenantId);
            return null;
        }

        // 3. Parse Parameters resource → FhirPatchOperation[]
        FhirPatchOperation[] operations;
        try
        {
            operations = _parametersParser.Parse(request.PatchDocument);
        }
        catch (FhirPatchException ex)
        {
            _logger.LogError(ex,
                "PATCH failed: Invalid Parameters resource for {ResourceType}/{ResourceId}",
                request.ResourceType,
                request.ResourceId);
            throw;
        }

        // 4. Validate patch operations structure
        try
        {
            _fhirPatchValidator.Validate(operations);
        }
        catch (FhirPatchException ex)
        {
            _logger.LogError(ex,
                "PATCH failed: Validation error for {ResourceType}/{ResourceId}",
                request.ResourceType,
                request.ResourceId);
            throw;
        }

        // 5. Deserialize existing resource from bytes
        var existingResource = JsonSourceNodeFactory.Parse(existing.ResourceBytes);
        if (existingResource == null)
        {
            throw new FhirPatchException("Failed to deserialize existing resource");
        }

        // 6. Clone resource before patching for immutable property validation
        var beforeClone = CloneResource(existingResource);

        // 7. Apply patch operations
        var patchedResource = await _patchEngine.ApplyPatchAsync(
            existingResource,
            operations,
            cancellationToken);

        // 8. Validate immutable properties have not changed
        try
        {
            _immutablePropertyValidator.Validate(beforeClone, patchedResource);
        }
        catch (FhirPatchException ex)
        {
            _logger.LogError(ex,
                "PATCH failed: Immutable property violation for {ResourceType}/{ResourceId}",
                request.ResourceType,
                request.ResourceId);
            throw;
        }

        // 9. Create updated ResourceWrapper
        var updated = new ResourceWrapper(
            patchedResource.ResourceType,
            patchedResource.Id ?? request.ResourceId,
            existing.VersionId, // Will be incremented by repository
            DateTimeOffset.UtcNow,
            patchedResource,
            new ResourceRequest(
                "PATCH",
                $"{request.ResourceType}/{request.ResourceId}"))
        {
            TenantId = request.TenantId,
            FhirVersion = "4.0", // Default to R4
        };

        // 10. Save via repository (increments versionId, updates lastUpdated)
        var saveResult = await repository.CreateOrUpdateAsync(updated, cancellationToken);

        // 11. Update patchedResource meta with saved version info
        patchedResource.Meta ??= new();
        patchedResource.Meta.VersionId = saveResult.Key.VersionId ?? "1";
        patchedResource.Meta.LastUpdated = saveResult.LastModified;

        // 12. Create final ResourceWrapper with updated meta
        var result = new ResourceWrapper(
            patchedResource.ResourceType,
            patchedResource.Id ?? request.ResourceId,
            saveResult.Key.VersionId ?? "1",
            saveResult.LastModified,
            patchedResource,
            new ResourceRequest(
                "PATCH",
                $"{request.ResourceType}/{request.ResourceId}"))
        {
            TenantId = request.TenantId,
            FhirVersion = "4.0", // Default to R4
        };

        _logger.LogInformation(
            "PATCH succeeded: {ResourceType}/{ResourceId} updated to version {Version} in tenant {TenantId}",
            request.ResourceType,
            request.ResourceId,
            saveResult.Key.VersionId,
            request.TenantId);

        return result;
    }

    /// <summary>
    /// Clone a ResourceJsonNode for immutable property validation.
    /// </summary>
    private static ResourceJsonNode CloneResource(ResourceJsonNode source)
    {
        var json = source.SerializeToString();
        return JsonSourceNodeFactory.Parse(json)
            ?? throw new FhirPatchException("Failed to clone resource for validation");
    }
}
