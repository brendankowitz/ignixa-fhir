// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.DataLayer.SqlEntityFramework.Entities;

/// <summary>
/// Entity Framework entity for background job storage (system-wide, partition 0).
/// Supports multiple job types (import, export, validate, etc.) with a unified schema.
/// Maps to the [dbo].[BackgroundJobs] table in system partition.
/// TenantId is now stored in the Definition/payload, not as a schema column.
/// </summary>
public class BackgroundJobEntity
{
    /// <summary>
    /// Primary key: JobId (UUID).
    /// System-wide unique identifier across all tenants.
    /// </summary>
    public string JobId { get; set; } = null!;

    /// <summary>
    /// Job type discriminator (1=Export, 2=Import, 3=Validate, etc.).
    /// </summary>
    public int JobType { get; set; }

    /// <summary>
    /// DurableTask orchestration instance ID for linking execution state.
    /// </summary>
    public string? OrchestrationInstanceId { get; set; }

    /// <summary>
    /// Job status: Queued, Running, Completed, Failed, Cancelled.
    /// </summary>
    public string Status { get; set; } = null!;

    /// <summary>
    /// Immutable job definition/input parameters (JSON).
    /// </summary>
    public string Definition { get; set; } = null!;

    /// <summary>
    /// Runtime progress tracking as JSON.
    /// Example: { processedResources: 1500, progressPercentage: 75.5, ... }
    /// </summary>
    public string? Progress { get; set; }

    /// <summary>
    /// Final results as JSON.
    /// Example: { totalResources: 2000, totalErrors: 5, errorFileUrl: "..." }
    /// </summary>
    public string? Result { get; set; }

    /// <summary>
    /// Job lifecycle timestamp: when created.
    /// </summary>
    public DateTimeOffset CreateDate { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Job lifecycle timestamp: when started processing (nullable).
    /// </summary>
    public DateTimeOffset? StartDate { get; set; }

    /// <summary>
    /// Job lifecycle timestamp: when finished (nullable).
    /// </summary>
    public DateTimeOffset? EndDate { get; set; }

    /// <summary>
    /// Heartbeat for monitoring active jobs.
    /// </summary>
    public DateTimeOffset HeartbeatDate { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Which orchestration worker processed this job.
    /// </summary>
    public string? Worker { get; set; }

    /// <summary>
    /// Human-readable error message for UI display.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// User requested cancellation.
    /// </summary>
    public bool CancelRequested { get; set; }
}
