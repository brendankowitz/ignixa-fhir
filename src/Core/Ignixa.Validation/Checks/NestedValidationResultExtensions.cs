// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Validation.Checks;

/// <summary>
/// Shared merge logic for the checks that descend into a nested
/// <see cref="Ignixa.Validation.Abstractions.ValidationSchema"/> - <see cref="NestedComplexTypeCheck"/>,
/// <see cref="ChoiceVariantNestedCheck"/>, and <see cref="ContainedResourceCheck"/> - and fold the
/// nested subtree's <see cref="ValidationResult"/> into their own issue list.
/// </summary>
internal static class NestedValidationResultExtensions
{
    /// <summary>
    /// Returns <paramref name="nestedResult"/>'s issues, unless the nested result is invalid and
    /// carries none of its own - in which case a single synthesized
    /// <see cref="ValidationIssue.UnexplainedNestedFailure"/> is returned instead, so a rejection
    /// propagated from a nested subtree is never silent about why. A valid result, or an invalid
    /// result that already has issues, passes through unchanged.
    /// </summary>
    /// <param name="nestedResult">The result of validating the nested subtree.</param>
    /// <param name="location">FHIRPath location of the nested subtree - used only when an issue must be synthesized.</param>
    /// <param name="subject">Human-readable description of what was being validated - used only when an issue must be synthesized.</param>
    /// <returns>The issues to fold into the caller's own issue list.</returns>
    internal static IReadOnlyList<ValidationIssue> IssuesOrSynthesizedFailure(
        this ValidationResult nestedResult,
        string location,
        string subject)
    {
        if (nestedResult.IsValid || nestedResult.Issues.Count > 0)
        {
            return nestedResult.Issues;
        }

        return [ValidationIssue.UnexplainedNestedFailure(location, subject)];
    }
}
