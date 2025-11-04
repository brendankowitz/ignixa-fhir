// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Models;

/// <summary>
/// Progress data for an import job, stored as JSON in BackgroundJob.Progress.
/// </summary>
public class ImportJobProgress
{
    /// <summary>
    /// Number of resources successfully processed.
    /// </summary>
    public int ProcessedResources { get; set; }

    /// <summary>
    /// Number of files processed.
    /// </summary>
    public int ProcessedFiles { get; set; }

    /// <summary>
    /// Current file being processed.
    /// </summary>
    public string? CurrentFile { get; set; }

    /// <summary>
    /// Progress percentage (0-100).
    /// </summary>
    public double ProgressPercentage { get; set; }
}
