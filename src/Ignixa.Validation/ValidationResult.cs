// <copyright file="ValidationResult.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.SourceNodeSerialization.Models;

namespace Ignixa.Validation;

/// <summary>
/// Result of a fast-path validation operation.
/// </summary>
public sealed record ValidationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationResult"/> class.
    /// </summary>
    /// <param name="isValid">Whether the resource passed validation.</param>
    /// <param name="issues">The collection of validation issues found.</param>
    public ValidationResult(bool isValid, IReadOnlyList<ValidationIssue> issues)
    {
        IsValid = isValid;
        Issues = issues ?? throw new ArgumentNullException(nameof(issues));
    }

    /// <summary>
    /// Gets a value indicating whether the resource passed validation.
    /// True if there are no errors or fatal issues.
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    /// Gets the collection of validation issues found.
    /// Empty if validation passed with no warnings or informational messages.
    /// </summary>
    public IReadOnlyList<ValidationIssue> Issues { get; }

    /// <summary>
    /// Gets a value indicating whether there are any error or fatal issues.
    /// </summary>
    public bool HasErrors => Issues.Any(i => i.Severity is IssueSeverity.Error or IssueSeverity.Fatal);

    /// <summary>
    /// Gets a value indicating whether there are any warnings.
    /// </summary>
    public bool HasWarnings => Issues.Any(i => i.Severity == IssueSeverity.Warning);

    /// <summary>
    /// Creates a successful validation result with no issues.
    /// </summary>
    /// <returns>A validation result indicating success.</returns>
    public static ValidationResult Success() => new(isValid: true, issues: Array.Empty<ValidationIssue>());

    /// <summary>
    /// Creates a failed validation result with the specified issues.
    /// </summary>
    /// <param name="issues">The validation issues.</param>
    /// <returns>A validation result indicating failure.</returns>
    public static ValidationResult Failure(IReadOnlyList<ValidationIssue> issues) => new(isValid: false, issues: issues);

    /// <summary>
    /// Creates a failed validation result with a single issue.
    /// </summary>
    /// <param name="issue">The validation issue.</param>
    /// <returns>A validation result indicating failure.</returns>
    public static ValidationResult Failure(ValidationIssue issue) => new(isValid: false, issues: new[] { issue });

    /// <summary>
    /// Combines multiple validation results into a single result.
    /// The combined result is valid only if all input results are valid.
    /// </summary>
    /// <param name="results">The validation results to combine.</param>
    /// <returns>A single validation result containing all issues from the input results.</returns>
    public static ValidationResult Combine(IEnumerable<ValidationResult> results)
    {
        var resultsList = results.ToList();
        if (!resultsList.Any())
        {
            return Success();
        }

        var allIssues = resultsList.SelectMany(r => r.Issues).ToList();
        var hasErrors = allIssues.Any(i => i.Severity is IssueSeverity.Error or IssueSeverity.Fatal);

        return new ValidationResult(isValid: !hasErrors, issues: allIssues);
    }

    /// <summary>
    /// Converts this validation result to a FHIR OperationOutcome resource.
    /// Follows HAPI FHIR patterns for ecosystem compatibility.
    /// </summary>
    /// <returns>An OperationOutcomeJsonNode resource with issues from this validation result.</returns>
    public OperationOutcomeJsonNode ToOperationOutcome()
    {
        var outcome = new OperationOutcomeJsonNode();
        var issueList = new List<OperationOutcomeJsonNode.IssueComponent>();

        foreach (var issue in Issues)
        {
            var issueComponent = new OperationOutcomeJsonNode.IssueComponent
            {
                Severity = MapSeverity(issue.Severity),
                Code = DetermineIssueType(issue.Code),
                Diagnostics = issue.Message
            };

            // Add expression using the new method to ensure persistence
            issueComponent.AddExpression(issue.Path);

            // Set details if available
            if (issue.Details != null)
            {
                var detailsNode = new CodeableConceptJsonNode
                {
                    Text = issue.Details.Text ?? string.Empty
                };

                if (issue.Details.Coding?.Any() == true)
                {
                    foreach (var coding in issue.Details.Coding)
                    {
                        detailsNode.AddCoding(new CodingJsonNode
                        {
                            System = coding.System ?? string.Empty,
                            Code = coding.Code ?? string.Empty,
                            Display = coding.Display ?? string.Empty
                        });
                    }
                }

                issueComponent.Details = detailsNode;
            }
            else
            {
                // Fallback: Use code as text
                issueComponent.Details = new CodeableConceptJsonNode
                {
                    Text = issue.Code
                };
            }

            issueList.Add(issueComponent);
        }

        // Use the new method to properly set the issues in the mutable node
        outcome.SetIssues(issueList);

        return outcome;
    }

    private static OperationOutcomeJsonNode.IssueSeverity MapSeverity(IssueSeverity severity)
    {
        return severity switch
        {
            IssueSeverity.Fatal => OperationOutcomeJsonNode.IssueSeverity.Fatal,
            IssueSeverity.Error => OperationOutcomeJsonNode.IssueSeverity.Error,
            IssueSeverity.Warning => OperationOutcomeJsonNode.IssueSeverity.Warning,
            IssueSeverity.Information => OperationOutcomeJsonNode.IssueSeverity.Information,
            _ => OperationOutcomeJsonNode.IssueSeverity.Error
        };
    }

    private static OperationOutcomeJsonNode.IssueType DetermineIssueType(string code)
    {
        // Map constraint keys to FHIR IssueType
        return code switch
        {
            "code-invalid" or "not-in-vs" or "invalid-code" => OperationOutcomeJsonNode.IssueType.CodeInvalid,
            "cardinality-violation" => OperationOutcomeJsonNode.IssueType.Required,
            var c when c.StartsWith("bdl-", StringComparison.Ordinal) => OperationOutcomeJsonNode.IssueType.Invariant,
            var c when c.StartsWith("ele-", StringComparison.Ordinal) => OperationOutcomeJsonNode.IssueType.Invariant,
            var c when c.StartsWith("ext-", StringComparison.Ordinal) => OperationOutcomeJsonNode.IssueType.Invariant,
            var c when c.StartsWith("dom-", StringComparison.Ordinal) => OperationOutcomeJsonNode.IssueType.Invariant,
            var c when c.StartsWith("ref-", StringComparison.Ordinal) => OperationOutcomeJsonNode.IssueType.Invariant,
            _ => OperationOutcomeJsonNode.IssueType.Invariant
        };
    }
}
