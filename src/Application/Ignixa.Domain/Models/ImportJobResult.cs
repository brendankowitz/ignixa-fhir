// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.Domain.Models;

/// <summary>
/// Result data for a completed/failed import job, stored as JSON in BackgroundJob.Result.
/// </summary>
public class ImportJobResult
{
    /// <summary>
    /// Confirmed imported resources reported by completed file activities.
    /// A lower bound when CountsAreComplete is false.
    /// </summary>
    [JsonRequired]
    public int TotalResources { get; set; }

    /// <summary>
    /// Total number of errors encountered.
    /// </summary>
    [JsonRequired]
    public int TotalErrors { get; set; }

    /// <summary>
    /// False after a fatal processing failure when not all worker outcomes were reported.
    /// </summary>
    public bool CountsAreComplete { get; set; } = true;

    /// <summary>
    /// Number of attempted resources whose storage outcome is uncertain, or null when unavailable.
    /// </summary>
    public int? ResourcesWithUnknownOutcome { get; set; } = 0;

    /// <summary>
    /// Blob path to the error log (if errors occurred); polling resolves a fresh provider URL.
    /// </summary>
    public string? ErrorFileUrl { get; set; }
}
