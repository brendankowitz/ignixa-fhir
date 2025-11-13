// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Abstractions;

namespace Ignixa.Domain.Models;

/// <summary>
/// Immutable export job definition (input parameters) for use with BackgroundJob<ExportJobDefinition>.
/// Represents the configuration of a FHIR bulk export operation.
/// TenantId is stored here (in the payload), not as a BackgroundJob property.
/// </summary>
public class ExportJobDefinition : IJobDefinition
{
    /// <summary>
    /// Tenant ID for multi-tenancy isolation (stored in definition payload, not schema).
    /// </summary>
    public required int TenantId { get; init; }

    /// <summary>
    /// Resource types to export. If empty, exports all types.
    /// </summary>
    public required IReadOnlyCollection<string> ResourceTypes { get; init; }

    /// <summary>
    /// Optional start date for filtering resources (exported resources updated >= this date).
    /// </summary>
    public DateTimeOffset? Since { get; init; }

    /// <summary>
    /// Optional type filters (FHIR search query strings per resource type).
    /// Format: { "Observation": "code=http://loinc.org|85354-9", "Condition": "category=encounter-diagnosis" }
    /// </summary>
    public required IReadOnlyDictionary<string, string> TypeFilters { get; init; }

    /// <summary>
    /// Output format for exported files (default: application/fhir+ndjson).
    /// </summary>
    public required string OutputFormat { get; init; }

    /// <summary>
    /// Base path where export files are written.
    /// Format: {baseDir}/{tenantId}/export/{jobId}/
    /// </summary>
    public required string OutputPath { get; init; }

    /// <summary>
    /// Optional ViewDefinition ID for Parquet export with schema transformation.
    /// When specified, must be used with OutputFormat = "application/vnd.apache.parquet".
    /// </summary>
    public string? ViewDefinitionId { get; init; }

    /// <summary>
    /// Optional: Group ID for Group-scoped export.
    /// </summary>
    public string? GroupId { get; set; }
}
