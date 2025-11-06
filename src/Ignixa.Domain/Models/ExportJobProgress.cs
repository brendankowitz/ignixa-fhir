// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Models;

/// <summary>
/// Progress data for an export job, stored as JSON in BackgroundJob.Progress.
/// </summary>
public class ExportJobProgress
{
    /// <summary>
    /// Progress percentage (0-100).
    /// </summary>
    public double ProgressPercentage { get; set; }

    /// <summary>
    /// Number of resources exported so far.
    /// </summary>
    public int ResourcesExported { get; set; }

    /// <summary>
    /// Number of bytes written to output files.
    /// </summary>
    public long BytesWritten { get; set; }

    /// <summary>
    /// Current phase: Initialization, WorkerExecution, Aggregation.
    /// </summary>
    public string? CurrentPhase { get; set; }
}
