// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Context for partition resolution (extracted from HttpContext to avoid web dependencies in Domain layer).
/// </summary>
public record PartitionResolutionContext
{
    /// <summary>
    /// Tenant ID extracted from route or context.
    /// </summary>
    public required int TenantId { get; init; }

    /// <summary>
    /// Optional tenant configuration (if needed for routing decisions).
    /// </summary>
    public TenantConfiguration? TenantConfiguration { get; init; }
}

/// <summary>
/// Determines which partition(s) to use for FHIR operations.
/// Inspired by HAPI FHIR's IInterceptorService with @Hook(Pointcut.STORAGE_PARTITION_IDENTIFY_READ/WRITE).
///
/// Key Design:
/// - Separate methods for read vs write operations (reads may target multiple partitions, writes always target one)
/// - Returns RequestPartition with partition IDs and mode
/// - Isolation mode: Always returns single partition from context
/// - Distributed mode (future): May return multiple partitions for fanout queries
/// </summary>
public interface IPartitionStrategy
{
    /// <summary>
    /// Determines which partition(s) to READ from based on request context.
    ///
    /// Isolation Mode:
    /// - Always returns single partition ID from context
    /// - Example: GET /tenant/0/Patient/{id} → partition [0]
    ///
    /// Distributed Mode (Phase 20.2+):
    /// - May return single partition if patient context is known
    /// - May return multiple partitions for broad queries (fanout)
    /// - Example: GET /Patient?name=Smith → partitions [0, 1, 2] (fanout to all shards)
    /// - Example: GET /Observation?patient=Patient/123 → partition [1] (route to patient's shard)
    /// </summary>
    /// <param name="context">Partition resolution context (tenant ID, configuration)</param>
    /// <param name="resourceType">The FHIR resource type being queried</param>
    /// <param name="queryParams">Query parameters from the request (for distributed mode routing)</param>
    /// <returns>RequestPartition with partition ID(s) to query</returns>
    RequestPartition DetermineReadPartition(
        PartitionResolutionContext context,
        string resourceType,
        IReadOnlyDictionary<string, string> queryParams);

    /// <summary>
    /// Determines which partition to WRITE to based on resource content.
    /// ALWAYS returns a single partition (writes cannot target multiple partitions).
    ///
    /// Isolation Mode:
    /// - Returns single partition ID from context
    /// - Example: PUT /tenant/0/Patient/123 → partition [0]
    ///
    /// Distributed Mode (Phase 20.2+):
    /// - Returns single partition based on sharding strategy
    /// - Example: PUT /Patient/123 → Hash(123) % shardCount → partition [1]
    /// - Example: POST /Observation with subject=Patient/123 → route to patient's shard
    /// </summary>
    /// <param name="context">Partition resolution context (tenant ID, configuration)</param>
    /// <param name="resource">The FHIR resource being written (as ISourceNode for structure access)</param>
    /// <returns>RequestPartition with single partition ID to write to</returns>
    RequestPartition DetermineWritePartition(
        PartitionResolutionContext context,
        ResourceJsonNode resource);
}
