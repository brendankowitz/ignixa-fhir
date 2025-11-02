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

/// <summary>
/// Output from the export worker activity.
/// Contains results of processing a single partition.
/// </summary>
public record ExportWorkerOutput(
    /// <summary>
    /// FHIR resource type that was exported.
    /// </summary>
    string ResourceType,

    /// <summary>
    /// Start of the surrogate ID range that was processed.
    /// </summary>
    long StartSurrogateId,

    /// <summary>
    /// End of the surrogate ID range that was processed.
    /// </summary>
    long EndSurrogateId,

    /// <summary>
    /// Total number of resources exported from this range.
    /// </summary>
    long ResourcesExported,

    /// <summary>
    /// Total bytes written to the output file.
    /// </summary>
    long BytesWritten);

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

/// <summary>
/// Output from the GetExportRanges activity.
/// Contains the surrogate ID ranges to be processed in parallel.
/// </summary>
public record GetExportRangesOutput(
    /// <summary>
    /// FHIR resource type.
    /// </summary>
    string ResourceType,

    /// <summary>
    /// List of non-overlapping, exhaustive surrogate ID ranges.
    /// Each (StartId, EndId) tuple represents one worker's partition.
    /// </summary>
    IReadOnlyList<(long StartId, long EndId)> Ranges);

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

/// <summary>
/// Coordinator orchestration output.
/// Contains final results of the entire export job.
/// </summary>
public record ExportCoordinatorOutput(
    /// <summary>
    /// Whether the export completed successfully.
    /// </summary>
    bool Success,

    /// <summary>
    /// Total resources exported across all workers.
    /// </summary>
    long TotalResourcesExported,

    /// <summary>
    /// Total bytes written across all output files.
    /// </summary>
    long TotalBytesWritten,

    /// <summary>
    /// Results from each worker (one per partition).
    /// Null if export failed or hasn't completed.
    /// </summary>
    IReadOnlyList<ExportWorkerOutput>? WorkerResults = null,

    /// <summary>
    /// Error message if export failed.
    /// Null if successful.
    /// </summary>
    string? ErrorMessage = null,

    /// <summary>
    /// Phase where failure occurred (if applicable).
    /// Helps distinguish between initialization failures vs worker failures.
    /// Null if successful or phase unknown.
    /// </summary>
    string? FailurePhase = null);
