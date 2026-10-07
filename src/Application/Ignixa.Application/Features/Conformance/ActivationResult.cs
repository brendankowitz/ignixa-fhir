// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Result of package activation, indicating success or validation failures.
/// </summary>
public record ActivationResult
{
    /// <summary>
    /// Whether activation succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Validation issues encountered during activation (if Success = false).
    /// </summary>
    public IReadOnlyList<ValidationIssue> Issues { get; init; } = [];

    /// <summary>
    /// Resource types that require reindexing after activation (if Success = true).
    /// </summary>
    public IReadOnlyList<string> PendingReindex { get; init; } = [];

    /// <summary>
    /// Whether activation is durable but local conformance consumers will refresh on a later synchronization.
    /// </summary>
    public bool LocalRefreshDeferred { get; init; }

    /// <summary>
    /// Whether activation is durable but at least one phase-two transition schedule will be retried by the watchdog.
    /// </summary>
    public bool TransitionSchedulingDeferred { get; init; }

    /// <summary>
    /// Creates a successful activation result.
    /// </summary>
    public static ActivationResult Succeeded(
        IReadOnlyList<string>? pendingReindex = null,
        bool localRefreshDeferred = false,
        bool transitionSchedulingDeferred = false) =>
        new()
        {
            Success = true,
            PendingReindex = pendingReindex ?? [],
            LocalRefreshDeferred = localRefreshDeferred,
            TransitionSchedulingDeferred = transitionSchedulingDeferred
        };

    /// <summary>
    /// Creates a failed activation result with validation issues.
    /// </summary>
    public static ActivationResult Failed(IReadOnlyList<ValidationIssue> issues) =>
        new() { Success = false, Issues = issues };
}

/// <summary>
/// Represents a validation issue encountered during package activation.
/// </summary>
public record ValidationIssue(
    string Code,
    string Message,
    string? ResourceType = null,
    string? ParameterCode = null);
