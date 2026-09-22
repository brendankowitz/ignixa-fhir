// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.Domain.Models;

/// <summary>
/// Result data for a completed/failed export job, stored as JSON in BackgroundJob.Result.
/// </summary>
public class ExportJobResult
{
    /// <summary>
    /// Total number of resources exported.
    /// </summary>
    [JsonRequired]
    public long TotalResources { get; set; }

    /// <summary>
    /// Exported files keyed by resource type and partition, or resource type for older jobs.
    /// </summary>
    [JsonRequired]
    public Dictionary<string, string> ExportedFiles { get; init; } = new();

    /// <summary>
    /// Resource counts keyed identically to ExportedFiles. Absent for older persisted jobs.
    /// </summary>
    public Dictionary<string, long>? ExportedFileCounts { get; init; }

    /// <summary>
    /// Completion timestamp.
    /// </summary>
    public DateTimeOffset CompletedAt { get; set; }
}
