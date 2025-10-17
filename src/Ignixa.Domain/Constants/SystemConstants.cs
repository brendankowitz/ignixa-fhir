// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Constants;

/// <summary>
/// System-wide constants for the FHIR server.
/// </summary>
public static class SystemConstants
{
    /// <summary>
    /// Partition 0 is reserved for system operations and MUST NOT be used for regular tenant data.
    ///
    /// System Operations:
    /// - Transaction ID allocation (all transaction IDs are allocated from Partition 0)
    /// - System-wide sequences and counters
    /// - Internal metadata and configuration
    ///
    /// Multi-Tenancy (ADR-2523 Phase 20):
    /// - DeferredWriteCoordinator allocates transaction IDs from this partition
    /// - Ensures globally unique transaction IDs across the entire system
    /// - Regular API requests to /tenant/0/ routes are rejected by TenantResolutionMiddleware
    ///
    /// Storage:
    /// - Partition 0 must have a repository configuration in appsettings.json
    /// - Marked with IsSystemPartition = true to distinguish from regular tenants
    /// - BaseDirectory typically set to "system" for clarity
    /// </summary>
    public const int SystemPartitionId = 0;
}
