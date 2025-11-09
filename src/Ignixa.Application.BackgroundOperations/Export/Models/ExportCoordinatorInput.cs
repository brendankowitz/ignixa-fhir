// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Export.Models;

/// <summary>
/// Coordinator orchestration input.
/// Initiates the entire export job.
/// </summary>
public record ExportCoordinatorInput(
    /// <summary>
    /// Unique job identifier.
    /// </summary>
    string JobId,

    /// <summary>
    /// Tenant that requested the export.
    /// </summary>
    int TenantId,

    /// <summary>
    /// Specific resource types to export (if empty, exports all types).
    /// </summary>
    IReadOnlyCollection<string> ResourceTypes,

    /// <summary>
    /// Optional: Resources modified since this datetime (FHIR _since parameter).
    /// If null, no since filter is applied.
    /// </summary>
    DateTimeOffset? Since = null,

    /// <summary>
    /// Optional: Per-type search filters (e.g., {"Patient": "birthdate=gt2000", "Observation": "status=final"}).
    /// Key is resource type, value is query string of search parameters.
    /// If null, no filters are applied.
    /// </summary>
    IReadOnlyDictionary<string, string>? TypeFilters = null,

    /// <summary>
    /// Optional: Number of surrogate ID ranges per resource type for parallel processing.
    /// Default is 6. Valid range: 1-16.
    /// More ranges = more parallelism but higher DurableTask overhead.
    /// Example: 6 types × 6 ranges = 36 concurrent workers.
    /// </summary>
    int NumberOfRangesPerType = 6);
