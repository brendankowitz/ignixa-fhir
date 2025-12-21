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
    /// Creates a successful activation result.
    /// </summary>
    public static ActivationResult Succeeded(IReadOnlyList<string>? pendingReindex = null) =>
        new() { Success = true, PendingReindex = pendingReindex ?? [] };

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
