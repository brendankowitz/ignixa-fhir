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
    /// Gets what this issue says about the analysability of the expression.
    /// </summary>
    /// <remarks>
    /// One value rather than a flag per outcome, so that "indeterminate" and "always empty" cannot both
    /// be asserted of the same issue. They are contradictory — the first admits the expression could not
    /// be analysed, the second reports a decided fact about it — and consumers act on the difference.
    /// Settable only through <see cref="Indeterminate"/> and <see cref="AlwaysEmpty"/>, which fix the
    /// severity these outcomes are defined at; an initializer can produce only
    /// <see cref="ValidationIssueKind.Ordinary"/>.
    /// </remarks>
    public ValidationIssueKind Kind { get; private init; }

    /// <summary>
    /// Gets whether the issue means static analysis was unable to determine validity.
    /// </summary>
    public bool IsIndeterminate => Kind == ValidationIssueKind.Indeterminate;

    /// <summary>
    /// Gets whether the issue reports a subexpression the analyzer proved yields the empty collection
    /// for every conformant input.
    /// </summary>
    /// <remarks>
    /// Carried as structured state rather than left to message matching because it is the only signal
    /// distinguishing a probable typo (<c>status</c> against a <c>Patient</c> root) from a correct
    /// expression — both are <see cref="ValidationIssueSeverity.Warning"/> and both leave
    /// <c>AnalysisResult.IsValid</c> true.
    /// </remarks>
    public bool IsAlwaysEmpty => Kind == ValidationIssueKind.AlwaysEmpty;

    /// <summary>
    /// Gets the location information (line, column, position).
    /// </summary>
    public string? Location { get; init; }

    /// <summary>
    /// Gets the expression fragment that caused the issue.
    /// </summary>
    public string? Expression { get; init; }

    /// <summary>
    /// Creates an issue reporting a subexpression that yields the empty collection for every conformant
    /// input.
    /// </summary>
    public static ValidationIssue AlwaysEmpty(string message, string? location = null, string? expression = null)
    {
        return new ValidationIssue
        {
            Severity = ValidationIssueSeverity.Warning,
            Message = message,
            Kind = ValidationIssueKind.AlwaysEmpty,
            Location = location,
            Expression = expression
        };
    }

    /// <summary>
    /// Creates an issue reporting that the expression could not be analysed, as distinct from being
    /// invalid.
    /// </summary>
    public static ValidationIssue Indeterminate(string message, string? location = null, string? expression = null)
    {
        return new ValidationIssue
        {
            Severity = ValidationIssueSeverity.Warning,
            Message = message,
            Kind = ValidationIssueKind.Indeterminate,
            Location = location,
            Expression = expression
        };
    }
}

/// <summary>
/// What a <see cref="ValidationIssue"/> says about the analysability of the expression it reports on.
/// </summary>
public enum ValidationIssueKind
{
    /// <summary>
    /// The issue makes no claim beyond its severity and message.
    /// </summary>
    Ordinary,

    /// <summary>
    /// Static analysis could not decide the expression, so its validity is unknown rather than refuted.
    /// </summary>
    Indeterminate,

    /// <summary>
    /// A subexpression was proved to yield the empty collection for every conformant input.
    /// </summary>
    AlwaysEmpty
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
