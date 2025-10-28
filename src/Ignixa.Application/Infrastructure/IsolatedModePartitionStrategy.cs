// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Partition strategy for Isolation mode (Phase 20).
/// Always returns a single partition ID from the partition resolution context.
///
/// In Isolation mode:
/// - Different organizations are separate tenants (Tenant 0 = Mayo Clinic, Tenant 1 = Cedars-Sinai)
/// - Each tenant has its own isolated data store
/// - Tenant ID is explicitly specified in the URL: /tenant/{tenantId}/{resourceType}/{id}
/// - Middleware extracts tenantId from route and passes via PartitionResolutionContext
/// </summary>
public class IsolatedModePartitionStrategy : IPartitionStrategy
{
    private readonly ILogger<IsolatedModePartitionStrategy> _logger;

    public IsolatedModePartitionStrategy(ILogger<IsolatedModePartitionStrategy> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public RequestPartition DetermineReadPartition(
        PartitionResolutionContext context,
        string resourceType,
        IReadOnlyDictionary<string, string> queryParams)
    {
        _logger.LogDebug(
            "Determined read partition for tenant {TenantId}, resourceType {ResourceType}",
            context.TenantId,
            resourceType);

        return new RequestPartition
        {
            PartitionIds = new[] { context.TenantId },
            Mode = PartitionMode.Isolated
        };
    }

    public RequestPartition DetermineWritePartition(
        PartitionResolutionContext context,
        ResourceJsonNode resource)
    {
        _logger.LogDebug(
            "Determined write partition for tenant {TenantId}, resourceType {ResourceType}",
            context.TenantId,
            resource.ResourceType);

        return new RequestPartition
        {
            PartitionIds = new[] { context.TenantId },
            Mode = PartitionMode.Isolated
        };
    }
}
