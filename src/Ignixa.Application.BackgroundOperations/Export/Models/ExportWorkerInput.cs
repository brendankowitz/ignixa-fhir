// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Export.Models;

/// <summary>
/// Input for the export worker activity.
/// Represents a single partition (resource type + surrogate ID range) to be processed.
/// </summary>
public record ExportWorkerInput(
    /// <summary>
    /// Unique identifier for the export job (used for tracking/logging).
    /// </summary>
    string JobId,

    /// <summary>
    /// Tenant ID that owns this export.
    /// </summary>
    int TenantId,

    /// <summary>
    /// FHIR resource type (e.g., "Patient", "Observation").
    /// </summary>
    string ResourceType,

    /// <summary>
    /// Start of the surrogate ID range (inclusive).
    /// </summary>
    long StartSurrogateId,

    /// <summary>
    /// End of the surrogate ID range (inclusive).
    /// </summary>
    long EndSurrogateId,

    /// <summary>
    /// Blob storage path where output NDJSON file will be written.
    /// Format: "tenant/{tenantId}/export/{jobId}/{resourceType}-{startId}-{endId}.ndjson"
    /// </summary>
    string OutputPath,

    /// <summary>
    /// Optional: Resources modified since this datetime (FHIR _since parameter).
    /// If null, no since filter is applied.
    /// </summary>
    DateTimeOffset? Since = null,

    /// <summary>
    /// Optional: Per-type search filters (e.g., {"Patient": "birthdate=gt2000", "Observation": "status=final"}).
    /// Key is resource type, value is query string of search parameters.
    /// If null or doesn't contain this resource type, no filters are applied.
    /// </summary>
    IReadOnlyDictionary<string, string>? TypeFilters = null);
