// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.Domain.Models;

/// <summary>
/// Generic background job metadata for DurableTask orchestrations (system-wide, partition 0).
/// Supports multiple job types (import, export, validate, etc.) with a unified schema.
/// TenantId is now stored in the Definition/payload, not as a model property.
/// </summary>
/// <typeparam name="T">The strongly-typed job definition/input parameters.</typeparam>
public class BackgroundJob<T> where T : class
{
    /// <summary>
    /// Unique job identifier (UUID).
    /// System-wide unique identifier across all tenants.
    /// </summary>
    public required string JobId { get; set; }

    /// <summary>
    /// Job type discriminator (1=Export, 2=Import, 3=Validate, etc.).
    /// </summary>
    public required int JobType { get; set; }

    /// <summary>
    /// DurableTask orchestration instance ID for linking execution state.
    /// </summary>
    public string? OrchestrationInstanceId { get; set; }

    /// <summary>
    /// Job status: Queued, Running, Completed, Failed, Cancelled.
    /// </summary>
    public required string Status { get; set; }

    /// <summary>
    /// Immutable job definition/input parameters (strongly typed).
    /// </summary>
    public required T Definition { get; set; }

    /// <summary>
    /// Runtime progress tracking as JSON (flexible schema).
    /// Example: { processedResources: 1500, processedFiles: 3, progressPercentage: 75.5, currentFile: "..." }
    /// </summary>
    public JsonNode? Progress { get; set; }

    /// <summary>
    /// Final results as JSON (flexible schema).
    /// Example: { totalResources: 2000, totalErrors: 5, errorFileUrl: "...", outputFiles: [...] }
    /// </summary>
    public JsonNode? Result { get; set; }

    /// <summary>
    /// Job lifecycle timestamps.
    /// </summary>
    public DateTimeOffset CreateDate { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartDate { get; set; }
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
