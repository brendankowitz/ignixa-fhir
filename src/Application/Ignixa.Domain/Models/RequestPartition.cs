// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Models;

/// <summary>
/// Represents the partition(s) determined for a FHIR request.
/// Inspired by HAPI FHIR's RequestPartitionId pattern.
/// </summary>
public record RequestPartition
{
    /// <summary>
    /// The partition IDs to target for this request.
    /// - Isolation mode: Always contains exactly 1 partition ID (the tenant ID)
    /// - Distributed mode: May contain multiple partition IDs for fanout queries
    /// </summary>
    public required IReadOnlyList<int> PartitionIds { get; init; }

    /// <summary>
    /// The partitioning mode for this request.
    /// </summary>
    public required PartitionMode Mode { get; init; }
}

/// <summary>
/// Defines the partitioning mode for tenant operations.
/// </summary>
public enum PartitionMode
{
    /// <summary>
    /// Isolation mode: Different organizations as separate tenants.
    /// Each tenant has its own isolated data store.
    /// Example: Tenant 0 = Mayo Clinic, Tenant 1 = Cedars-Sinai, Tenant 2 = Johns Hopkins
    /// </summary>
    Isolated,

    /// <summary>
    /// Distributed mode: Single organization with data sharded across multiple stores.
    /// Used for horizontal scaling of a single customer's data.
    /// Example: Acme Hospital with 100M patients sharded across 3 data stores
    /// Phase 20.2+: Not implemented yet
    /// </summary>
    Distributed
}
