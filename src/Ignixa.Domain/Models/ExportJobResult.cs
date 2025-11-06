// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Models;

/// <summary>
/// Result data for a completed/failed export job, stored as JSON in BackgroundJob.Result.
/// </summary>
public class ExportJobResult
{
    /// <summary>
    /// Total number of resources exported.
    /// </summary>
    public int TotalResources { get; set; }

    /// <summary>
    /// Exported files as a dictionary: resourceType -> filePath.
    /// </summary>
    public Dictionary<string, string> ExportedFiles { get; init; } = new();

    /// <summary>
    /// Completion timestamp.
    /// </summary>
    public DateTimeOffset CompletedAt { get; set; }
}
