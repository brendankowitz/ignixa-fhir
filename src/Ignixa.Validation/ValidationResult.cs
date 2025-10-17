// <copyright file="ValidationResult.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

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
}
