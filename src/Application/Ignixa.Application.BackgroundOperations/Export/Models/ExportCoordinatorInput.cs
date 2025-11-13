// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Constants;

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
    int NumberOfRangesPerType = 6,

    /// <summary>
    /// Optional: Output format for export files.
    /// Supported values: <see cref="ExportConstants.MediaTypeNdjson"/> (default) or <see cref="ExportConstants.MediaTypeParquet"/>.
    /// Determines the file extension (.ndjson or .parquet).
    /// </summary>
    string OutputFormat = ExportConstants.MediaTypeNdjson,

    /// <summary>
    /// Optional: ViewDefinition ID for Parquet export with schema transformation.
    /// When specified, must be used with OutputFormat = <see cref="ExportConstants.MediaTypeParquet"/>.
    /// </summary>
    string? ViewDefinitionId = null,

    /// <summary>
    /// Optional: Group ID for Group-scoped export.
    /// When specified, only exports resources for patients that are members of this Group.
    /// </summary>
    string? GroupId = null);
