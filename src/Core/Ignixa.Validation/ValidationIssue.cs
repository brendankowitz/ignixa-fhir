// <copyright file="ValidationIssue.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Globalization;
using System.Text.Json.Nodes;
using Ignixa.Models;

namespace Ignixa.Validation;

/// <summary>
/// Represents a single validation issue found during resource validation.
/// Aligned with HAPI FHIR OperationOutcome structure for ecosystem compatibility.
/// </summary>
public sealed record ValidationIssue
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationIssue"/> class.
    /// </summary>
    /// <param name="severity">The severity of the issue.</param>
    /// <param name="code">The constraint key (e.g., "bdl-7", "ele-1") or issue code.</param>
    /// <param name="path">The element path where the issue was found (e.g., "Patient.name[0]").</param>
    /// <param name="message">A human-readable description of the issue.</param>
    public ValidationIssue(IssueSeverity severity, string code, string path, string message)
    {
        Severity = severity;
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationIssue"/> class (backward-compatible constructor).
    /// </summary>
    /// <param name="severity">The severity of the issue.</param>
    /// <param name="path">The element path where the issue was found.</param>
    /// <param name="message">A human-readable description of the issue.</param>
    public ValidationIssue(IssueSeverity severity, string path, string message)
        : this(severity, "validation-error", path, message)
    {
    }

    /// <summary>
    /// Gets the severity of the validation issue.
    /// </summary>
    public IssueSeverity Severity { get; init; }

    /// <summary>
    /// Gets the constraint key (e.g., "bdl-7", "ele-1") or issue code (e.g., "code-invalid").
    /// Used for OperationOutcome.issue.details.text and programmatic issue identification.
    /// </summary>
    /// <example>
    /// "bdl-7", "ele-1", "ext-1", "code-invalid", "not-in-vs"
    /// </example>
    public string Code { get; init; }

    /// <summary>
    /// Gets the element path where the issue was found (FHIRPath expression).
    /// </summary>
    /// <example>
    /// "Patient.name[0]", "Observation.value", "Bundle.entry[2].resource"
    /// </example>
    public string Path { get; init; }

    /// <summary>
    /// Gets the human-readable description of the issue.
    /// For invariant failures, should follow pattern: "{code}: {description}"
    /// </summary>
    /// <example>
    /// "bdl-7: FullUrl must be unique in a bundle...",
    /// "ele-1: All FHIR elements must have a @value or children"
    /// </example>
    public string Message { get; init; }

    /// <summary>
    /// Gets optional coded details for structured issue information (e.g., terminology issue types).
    /// Used for OperationOutcome.issue.details.coding.
    /// </summary>
    public CodeableConcept? Details { get; init; }

    /// <summary>
    /// Gets the stable identifier of the <see cref="IssueTemplate"/> <see cref="Message"/> was rendered from, or null when the
    /// issue was built from free text. Consumers that render their own text, or must not echo instance or definition content,
    /// key on this rather than parsing <see cref="Message"/>. The validator's templates are listed in <see cref="IssueTemplates"/>.
    /// </summary>
    public string? TemplateId { get; init; }

    /// <summary>
    /// Gets the values the template's placeholders were filled with, in placeholder order; empty when <see cref="TemplateId"/>
    /// is null. Each argument may be instance or definition content and is untrusted.
    /// </summary>
    public IReadOnlyList<string> TemplateArguments { get; init; } = [];

    /// <summary>
    /// Creates an issue whose message is rendered from <paramref name="template"/>, recording the template and its arguments.
    /// </summary>
    /// <param name="severity">The severity of the issue.</param>
    /// <param name="code">The constraint key or issue code.</param>
    /// <param name="path">The element path where the issue was found.</param>
    /// <param name="template">The message template.</param>
    /// <param name="arguments">The template's arguments, in placeholder order.</param>
    /// <returns>The issue.</returns>
    /// <exception cref="ArgumentException">The number of arguments differs from the template's.</exception>
    public static ValidationIssue FromTemplate(IssueSeverity severity, string code, string path, IssueTemplate template, params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(arguments);

        return new ValidationIssue(severity, code, path, template.Render(arguments))
        {
            TemplateId = template.Id,
            TemplateArguments = arguments,
        };
    }

    /// <summary>
    /// Creates an invariant failure issue with HAPI-compatible formatting.
    /// </summary>
    /// <param name="constraintKey">The constraint key (e.g., "bdl-7", "ele-1").</param>
    /// <param name="description">Human-readable description of the constraint.</param>
    /// <param name="location">FHIRPath location where the issue was found.</param>
    /// <param name="severity">Severity level (default: Error).</param>
    /// <returns>A validation issue formatted for OperationOutcome output.</returns>
    public static ValidationIssue InvariantFailure(
        string constraintKey,
        string description,
        string location,
        IssueSeverity severity = IssueSeverity.Error)
    {
        return FromTemplate(severity, constraintKey, location, IssueTemplates.Invariant, constraintKey, description);
    }

    /// <summary>
    /// Creates the issue reported when traversal stops at
    /// <see cref="ValidationState.MaxNestingDepth"/>. A Warning, not an Error: the instance has not
    /// been shown to be invalid, only left partly unexamined — but the gap is stated rather than
    /// passed off as a clean result.
    /// </summary>
    /// <param name="location">FHIRPath location of the element whose subtree was not descended into.</param>
    /// <param name="elementName">The element whose subtree was left unvalidated.</param>
    /// <returns>A non-failing validation issue describing the truncated traversal.</returns>
    public static ValidationIssue NestingLimitExceeded(string location, string elementName)
    {
        return FromTemplate(
            IssueSeverity.Warning,
            "validation-nesting-limit",
            string.IsNullOrEmpty(location) ? elementName : $"{location}.{elementName}",
            IssueTemplates.NestingLimit,
            elementName,
            ValidationState.MaxNestingDepth.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Creates the issue synthesized when a nested validation result is invalid but carries none of
    /// its own issues, so an invalid outcome is never propagated without a reason. Always an Error:
    /// the nested result has already declared itself invalid, this only supplies the missing
    /// explanation - it does not change, or upgrade a Warning into, the verdict.
    /// </summary>
    /// <param name="location">FHIRPath location of the nested subtree that failed.</param>
    /// <param name="subject">Human-readable description of what was being validated (element, choice
    /// variant, or contained resource), so the message names what failed rather than saying only that
    /// something did.</param>
    /// <returns>A failing validation issue explaining an otherwise-unexplained nested rejection.</returns>
    public static ValidationIssue UnexplainedNestedFailure(string location, string subject)
    {
        return FromTemplate(IssueSeverity.Error, "validation-nested-unexplained", location, IssueTemplates.NestedUnexplained, subject);
    }

    /// <summary>
    /// Creates a terminology validation failure issue with HAPI-compatible coding.
    /// </summary>
    /// <param name="code">The code that failed validation.</param>
    /// <param name="system">The code system URL.</param>
    /// <param name="valueSet">The ValueSet URL (optional).</param>
    /// <param name="location">FHIRPath location where the issue was found.</param>
    /// <param name="issueType">Terminology issue type code (e.g., "not-in-vs", "invalid-code").</param>
    /// <returns>A validation issue with structured terminology details.</returns>
    public static ValidationIssue TerminologyFailure(
        string code,
        string system,
        string? valueSet,
        string location,
        string issueType = "not-in-vs")
    {
        (IssueTemplate template, string[] arguments) = valueSet is { } named
            ? (IssueTemplates.CodeNotInValueSet, new[] { system, code, named })
            : (IssueTemplates.CodeUnknownInSystem, new[] { code, system });
        var message = template.Render(arguments);

        var details = new CodeableConcept()
        {
            Text = message
        };

        // Add coding using the new method to ensure persistence
        details.Coding.Add(new Coding()
        {
            System = "http://hl7.org/fhir/tools/CodeSystem/tx-issue-type",
            Code = issueType
        });

        return new ValidationIssue(
            severity: IssueSeverity.Error,
            code: "code-invalid",
            path: location,
            message: message)
        {
            Details = details,
            TemplateId = template.Id,
            TemplateArguments = arguments,
        };
    }

    /// <summary>
    /// Creates a cardinality violation issue.
    /// </summary>
    /// <param name="elementPath">The element path that violated cardinality.</param>
    /// <param name="min">Minimum cardinality.</param>
    /// <param name="max">Maximum cardinality.</param>
    /// <param name="actual">Actual occurrence count.</param>
    /// <returns>A validation issue for cardinality violation.</returns>
    public static ValidationIssue CardinalityViolation(
        string elementPath,
        int? min,
        int? max,
        int actual)
    {
        var culture = CultureInfo.InvariantCulture;
        var found = actual.ToString(culture);
        (IssueTemplate template, string[] arguments) = (min, max) switch
        {
            ({ } atLeast, _) when actual < atLeast => (IssueTemplates.CardinalityBelowMinimum, new[] { elementPath, atLeast.ToString(culture), found }),
            (_, { } atMost) when actual > atMost => (IssueTemplates.CardinalityAboveMaximum, new[] { elementPath, atMost.ToString(culture), found }),
            _ => (IssueTemplates.CardinalityViolation, new[] { elementPath, min?.ToString(culture) ?? string.Empty, max?.ToString(culture) ?? string.Empty, found }),
        };

        return FromTemplate(IssueSeverity.Error, "cardinality-violation", elementPath, template, arguments);
    }
}
