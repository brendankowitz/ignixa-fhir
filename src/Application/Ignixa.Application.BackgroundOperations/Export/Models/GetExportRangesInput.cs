// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Export.Models;

/// <summary>
/// Input for the GetExportRanges activity.
/// Used by the coordinator to determine partitioning for a resource type.
/// </summary>
public record GetExportRangesInput(
    /// <summary>
    /// Tenant ID (used for database context).
    /// </summary>
    int TenantId,

    /// <summary>
    /// FHIR resource type to partition.
    /// </summary>
    string ResourceType,

    /// <summary>
    /// Number of ranges to create (e.g., 4-8 for parallelism).
    /// </summary>
    int NumberOfRanges);
