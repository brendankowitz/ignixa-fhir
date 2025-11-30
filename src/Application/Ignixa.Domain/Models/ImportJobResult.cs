// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Models;

/// <summary>
/// Result data for a completed/failed import job, stored as JSON in BackgroundJob.Result.
/// </summary>
public class ImportJobResult
{
    /// <summary>
    /// Total number of resources imported.
    /// </summary>
    public int TotalResources { get; set; }

    /// <summary>
    /// Total number of errors encountered.
    /// </summary>
    public int TotalErrors { get; set; }

    /// <summary>
    /// URL to error log file (if errors occurred).
    /// </summary>
    public string? ErrorFileUrl { get; set; }
}
