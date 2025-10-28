// <copyright file="ValidationIssue.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Serialization.Models;

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
    public CodeableConceptJsonNode? Details { get; init; }

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
        return new ValidationIssue(
            severity: severity,
            code: constraintKey,
            path: location,
            message: $"{constraintKey}: {description}");
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
        var message = valueSet != null
            ? $"The provided code '{system}#{code}' was not found in the value set '{valueSet}'"
            : $"Unknown code '{code}' in the CodeSystem '{system}'";

        var details = new CodeableConceptJsonNode
        {
            Text = message
        };

        // Add coding using the new method to ensure persistence
        details.AddCoding(new CodingJsonNode
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
            Details = details
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
        string message;
        if (min.HasValue && actual < min.Value)
        {
            message = $"{elementPath} must have at least {min.Value} occurrence(s), but found {actual}";
        }
        else if (max.HasValue && actual > max.Value)
        {
            message = $"{elementPath} must have at most {max.Value} occurrence(s), but found {actual}";
        }
        else
        {
            message = $"{elementPath} cardinality violation: expected {min}..{max}, found {actual}";
        }

        return new ValidationIssue(
            severity: IssueSeverity.Error,
            code: "cardinality-violation",
            path: elementPath,
            message: message);
    }
}
