// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirPath.Visitors;

/// <summary>
/// Represents a validation issue found during FhirPath static analysis.
/// </summary>
public sealed record ValidationIssue
{
    /// <summary>
    /// Gets the severity of the issue.
    /// </summary>
    public required ValidationIssueSeverity Severity { get; init; }

    /// <summary>
    /// Gets the issue message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets whether the issue means static analysis was unable to determine validity.
    /// </summary>
    public bool IsIndeterminate { get; init; }

    /// <summary>
    /// Gets the location information (line, column, position).
    /// </summary>
    public string? Location { get; init; }

    /// <summary>
    /// Gets the expression fragment that caused the issue.
    /// </summary>
    public string? Expression { get; init; }
}

/// <summary>
/// The severity level of a validation issue.
/// </summary>
public enum ValidationIssueSeverity
{
    /// <summary>
    /// Informational message that doesn't indicate a problem.
    /// </summary>
    Information,

    /// <summary>
    /// Warning that indicates a potential issue or an expression that cannot be fully analysed.
    /// </summary>
    Warning,

    /// <summary>
    /// Error that indicates the expression is invalid.
    /// </summary>
    Error
}
