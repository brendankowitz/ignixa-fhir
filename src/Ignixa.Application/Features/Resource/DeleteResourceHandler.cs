// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Features.Resource;

/// <summary>
/// Generic handler for deleting any FHIR resource.
/// Performs soft delete per FHIR R4 specification (creates tombstone version with IsDeleted=true).
/// </summary>
public class DeleteResourceHandler : IRequestHandler<DeleteResourceCommand, bool>
{
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly IPartitionStrategy _partitionStrategy;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<DeleteResourceHandler> _logger;

    public DeleteResourceHandler(
        IFhirRepositoryFactory repositoryFactory,
        IPartitionStrategy partitionStrategy,
        IHttpContextAccessor httpContextAccessor,
        ILogger<DeleteResourceHandler> logger)
    {
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _partitionStrategy = partitionStrategy ?? throw new ArgumentNullException(nameof(partitionStrategy));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> HandleAsync(DeleteResourceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Extract tenant ID from HttpContext (populated by TenantResolutionMiddleware)
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HttpContext is null");

        if (!httpContext.Items.TryGetValue("TenantId", out var tenantIdObj) || tenantIdObj is not int tenantId)
        {
            throw new InvalidOperationException(
                "TenantId not found in HttpContext.Items. TenantResolutionMiddleware may not have run.");
        }

        _logger.LogDebug(
            "Deleting resource {ResourceType}/{Id} in tenant {TenantId}",
            command.ResourceType,
            command.Id,
            tenantId);

        var tenantConfig = httpContext.Items["TenantConfiguration"] as TenantConfiguration;

        // Create partition resolution context
        var partitionContext = new PartitionResolutionContext
        {
            TenantId = tenantId,
            TenantConfiguration = tenantConfig
        };

        // Create minimal ResourceJsonNode for partition strategy (only resourceType/id needed)
        var minimalResourceNode = new ResourceJsonNode
        {
            ResourceType = command.ResourceType,
            Id = command.Id
        };

        // Get partition for this delete operation
        var partition = _partitionStrategy.DetermineWritePartition(partitionContext, minimalResourceNode);

        // Validate single partition (writes always go to one partition)
        if (partition.PartitionIds.Count != 1)
        {
            _logger.LogError(
                "Delete operation requires exactly 1 partition, received {Count} partition IDs",
                partition.PartitionIds.Count);
            throw new InvalidOperationException(
                $"Delete operation requires exactly 1 partition, received {partition.PartitionIds.Count} partition IDs");
        }

        int resolvedTenantId = partition.PartitionIds[0];

        _logger.LogDebug(
            "Partition determined for delete: {TenantId} (Mode: {Mode})",
            resolvedTenantId,
            partition.Mode);

        // Get tenant-specific repository
        var repository = await _repositoryFactory.GetRepositoryAsync(resolvedTenantId, cancellationToken);

        // Create resource key (positional parameters: ResourceType, Id, VersionId, TenantId)
        var key = new ResourceKey(command.ResourceType, command.Id, null, tenantId);

        // Create resource request metadata
        var request = new ResourceRequest("DELETE", $"{command.ResourceType}/{command.Id}");

        // Perform soft delete (creates tombstone version)
        var deletedKey = await repository.DeleteAsync(key, request, transactionId: null, cancellationToken);

        if (deletedKey == null)
        {
            // Resource never existed - return false (404 Not Found)
            _logger.LogWarning(
                "Cannot delete {ResourceType}/{Id}: resource not found in tenant {TenantId}",
                command.ResourceType,
                command.Id,
                tenantId);
            return false;
        }

        _logger.LogInformation(
            "Deleted {ResourceType}/{Id} (version {VersionId}) in tenant {TenantId}",
            command.ResourceType,
            command.Id,
            deletedKey.VersionId,
            tenantId);

        return true;
    }
}
