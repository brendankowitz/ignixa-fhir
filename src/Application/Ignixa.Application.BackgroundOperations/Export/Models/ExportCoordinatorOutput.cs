// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Export.Models;

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
