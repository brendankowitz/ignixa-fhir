using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.SourceNodeSerialization.SourceNodes.Models;
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
    private readonly ILogger<PatchResourceHandler> _logger;

    public PatchResourceHandler(
        IFhirRepositoryFactory repositoryFactory,
        FhirPatchParametersParser parametersParser,
        FhirPatchEngine patchEngine,
        ILogger<PatchResourceHandler> logger)
    {
        _repositoryFactory = repositoryFactory;
        _parametersParser = parametersParser;
        _patchEngine = patchEngine;
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

        // 4. Deserialize existing resource from bytes
        var existingJson = System.Text.Encoding.UTF8.GetString(existing.ResourceBytes.Span);
        var existingResource = JsonSerializer.Deserialize<ResourceJsonNode>(existingJson);
        if (existingResource == null)
        {
            throw new FhirPatchException("Failed to deserialize existing resource");
        }

        // 5. Apply patch operations
        var patchedResource = await _patchEngine.ApplyPatchAsync(
            existingResource,
            operations,
            cancellationToken);

        // 6. Create updated ResourceWrapper
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

        // 7. Save via repository (increments versionId, updates lastUpdated)
        var savedKey = await repository.CreateOrUpdateAsync(updated, cancellationToken);

        // 8. Update patchedResource meta with saved version info
        patchedResource.Meta ??= new();
        patchedResource.Meta.VersionId = savedKey.VersionId ?? "1";
        patchedResource.Meta.LastUpdated = DateTimeOffset.UtcNow;

        // 9. Create final ResourceWrapper with updated meta
        var result = new ResourceWrapper(
            patchedResource.ResourceType,
            patchedResource.Id ?? request.ResourceId,
            savedKey.VersionId ?? "1",
            patchedResource.Meta.LastUpdated.Value,
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
            savedKey.VersionId,
            request.TenantId);

        return result;
    }
}
