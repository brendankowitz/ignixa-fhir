// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.Extensions.Logging;
using Ignixa.Application.Infrastructure;
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
    private readonly IFhirRequestContextAccessor _contextAccessor;
    private readonly ILogger<DeleteResourceHandler> _logger;

    public DeleteResourceHandler(
        IFhirRepositoryFactory repositoryFactory,
        IPartitionStrategy partitionStrategy,
        IFhirRequestContextAccessor contextAccessor,
        ILogger<DeleteResourceHandler> logger)
    {
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _partitionStrategy = partitionStrategy ?? throw new ArgumentNullException(nameof(partitionStrategy));
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> HandleAsync(DeleteResourceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Get FHIR request context (populated by FhirRequestContextMiddleware)
        var context = _contextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available");

        _logger.LogDebug(
            "Deleting resource {ResourceType}/{Id} in tenant {TenantId}",
            command.ResourceType,
            command.Id,
            context.TenantId);

        // Create partition resolution context from FHIR request context
        var partitionContext = new PartitionResolutionContext
        {
            TenantId = context.TenantId,
            TenantConfiguration = context.TenantConfiguration
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
        var key = new ResourceKey(command.ResourceType, command.Id, null, context.TenantId);

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
                context.TenantId);
            return false;
        }

        _logger.LogInformation(
            "Deleted {ResourceType}/{Id} (version {VersionId}) in tenant {TenantId}",
            command.ResourceType,
            command.Id,
            deletedKey.VersionId,
            context.TenantId);

        return true;
    }
}
