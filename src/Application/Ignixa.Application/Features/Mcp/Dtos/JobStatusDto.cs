// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Mcp.Dtos;

/// <summary>
/// DTO representing detailed job status for MCP tool responses.
/// Includes full job definition and result information.
/// </summary>
public class JobStatusDto
{
    /// <summary>
    /// Unique job identifier.
    /// </summary>
    public string JobId { get; init; } = string.Empty;

    /// <summary>
    /// Job type: "Import" or "Export".
    /// </summary>
    public string JobType { get; init; } = string.Empty;

    /// <summary>
    /// Current job status: "Queued", "Running", "Completed", "Failed", "Cancelled".
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Progress percentage (0-100). Only populated for running jobs.
    /// </summary>
    public double? ProgressPercentage { get; init; }

    /// <summary>
    /// Human-readable progress description.
    /// </summary>
    public string? ProgressDescription { get; init; }

    /// <summary>
    /// Job creation timestamp.
    /// </summary>
    public DateTimeOffset CreateDate { get; init; }

    /// <summary>
    /// Job start timestamp (null if not started).
    /// </summary>
    public DateTimeOffset? StartDate { get; init; }

    /// <summary>
    /// Job completion/failure timestamp (null if not finished).
    /// </summary>
    public DateTimeOffset? EndDate { get; init; }

    /// <summary>
    /// Error message if job failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Job definition (input parameters).
    /// For Import: inputFormat, inputSource, mode, inputFiles count.
    /// For Export: resourceTypes, since, outputFormat, outputPath.
    /// </summary>
    public object? Definition { get; init; }

    /// <summary>
    /// Job result (output data). Only populated when status is Completed.
    /// For Import: totalResources, totalErrors, errorFileUrl.
    /// For Export: file URLs, resource counts.
    /// </summary>
    public object? Result { get; init; }
}
