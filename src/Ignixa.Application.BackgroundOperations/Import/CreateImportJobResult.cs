// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Import;

/// <summary>
/// Result of creating a new import job.
/// Contains job identifier and initial status information.
/// </summary>
public record CreateImportJobResult
{
    /// <summary>
    /// Unique job identifier for tracking.
    /// </summary>
    public required string JobId { get; init; }

    /// <summary>
    /// Initial job status (typically "Queued").
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Orchestration instance ID from DurableTask.
    /// </summary>
    public required string OrchestrationInstanceId { get; init; }

    /// <summary>
    /// Job creation timestamp.
    /// </summary>
    public required DateTimeOffset CreateDate { get; init; }
}
