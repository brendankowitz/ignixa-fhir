// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;

namespace Ignixa.Application.BackgroundOperations.Export;

/// <summary>
/// Command to create and start a new FHIR bulk export job.
/// Exports resources to NDJSON files in blob storage.
/// </summary>
public record CreateExportJobCommand : IRequest<CreateExportJobResult>
{
    /// <summary>
    /// Tenant ID for multi-tenancy isolation.
    /// </summary>
    public required int TenantId { get; init; }

    /// <summary>
    /// Resource types to export. If empty, exports all types.
    /// </summary>
    public required IReadOnlyCollection<string> ResourceTypes { get; init; }

    /// <summary>
    /// Export only resources modified since this date (optional).
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
    public string OutputFormat { get; init; } = "application/fhir+ndjson";
}
