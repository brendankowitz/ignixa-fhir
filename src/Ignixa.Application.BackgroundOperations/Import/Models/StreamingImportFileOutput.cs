// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Import.Models;

/// <summary>
/// Output from StreamingImportFileActivity.
/// Reports success/error counts and performance metrics for a single file import.
/// </summary>
public record StreamingImportFileOutput
{
    public required string FileUrl { get; init; }
    public required string ResourceType { get; init; }
    public required int SuccessCount { get; init; }
    public required int ErrorCount { get; init; }
    public required TimeSpan Duration { get; init; }
    public required IReadOnlyList<ImportErrorLogEntry> Errors { get; init; }

    /// <summary>
    /// Throughput in resources per second.
    /// </summary>
    public double ResourcesPerSecond =>
        Duration.TotalSeconds > 0 ? SuccessCount / Duration.TotalSeconds : 0;
}
