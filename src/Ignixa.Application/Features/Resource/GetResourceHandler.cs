// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.Extensions.Logging;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;

namespace Ignixa.Application.Features.Resource;

/// <summary>
/// Generic handler for retrieving any FHIR resource by type and ID.
/// Multi-tenant enabled: Uses IPartitionStrategy + IFhirRepositoryFactory.
/// Returns SearchEntryResult for zero-copy serialization (read path with raw bytes).
///
/// Flow:
/// 1. Get tenant context from IFhirRequestContextAccessor
/// 2. Determine partition using IPartitionStrategy
/// 3. Validate single partition (CRUD always targets one partition)
/// 4. Get repository from IFhirRepositoryFactory
/// 5. Execute GetAsync directly (no execution strategy for CRUD)
/// </summary>
public class GetResourceHandler : IRequestHandler<GetResourceQuery, SearchEntryResult?>
{
    private readonly IPartitionStrategy _partitionStrategy;
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly IFhirRequestContextAccessor _contextAccessor;
    private readonly ILogger<GetResourceHandler> _logger;

    public GetResourceHandler(
        IPartitionStrategy partitionStrategy,
        IFhirRepositoryFactory repositoryFactory,
        IFhirRequestContextAccessor contextAccessor,
        ILogger<GetResourceHandler> logger)
    {
        _partitionStrategy = partitionStrategy ?? throw new ArgumentNullException(nameof(partitionStrategy));
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SearchEntryResult?> HandleAsync(GetResourceQuery query, CancellationToken cancellationToken)
    {
        // Get FHIR request context (populated by FhirRequestContextMiddleware)
        var context = _contextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available");

        _logger.LogDebug("Processing GetResource for {ResourceType}/{Id}", query.ResourceType, query.Id);

        // Create partition resolution context from FHIR request context
        var partitionContext = new PartitionResolutionContext
        {
            TenantId = context.TenantId,
            TenantConfiguration = context.TenantConfiguration
        };

        // 1. Determine partition using IPartitionStrategy
        var partition = _partitionStrategy.DetermineReadPartition(
            partitionContext,
            query.ResourceType,
            queryParams: new Dictionary<string, string>());

        // 2. Validate single partition (CRUD always targets one partition)
        if (partition.PartitionIds.Count != 1)
        {
            _logger.LogError(
                "Get operation requires exactly 1 partition, received {Count} partition IDs",
                partition.PartitionIds.Count);
            throw new InvalidOperationException(
                $"Get operation requires exactly 1 partition, received {partition.PartitionIds.Count} partition IDs");
        }

        int resolvedTenantId = partition.PartitionIds[0];

        _logger.LogDebug(
            "Partition determined: {TenantId} (Mode: {Mode})",
            resolvedTenantId,
            partition.Mode);

        // 3. Get repository from factory
        var repository = await _repositoryFactory.GetRepositoryAsync(resolvedTenantId, cancellationToken);

        // 4. Execute GetAsync directly - returns SearchEntryResult with raw bytes for zero-copy serialization
        var key = new ResourceKey(query.ResourceType, query.Id);
        SearchEntryResult? result = await repository.GetAsync(key, cancellationToken);

        if (result == null)
        {
            _logger.LogInformation("{ResourceType} not found: {Id} in tenant {TenantId}", query.ResourceType, query.Id, resolvedTenantId);
        }
        else
        {
            _logger.LogDebug("Retrieved {ResourceType}/{Id} version {VersionId} from tenant {TenantId}",
                result.ResourceType, result.ResourceId, result.VersionId, resolvedTenantId);
        }

        return result;
    }
}
