// <copyright file="NarrativeCheck.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates FHIR Narrative (text) structure.
/// Ensures text.status is present and valid.
/// Ensures text.div is present when status is not 'empty'.
/// Tier 1 (Fast) validator.
/// </summary>
public class NarrativeCheck : IValidationCheck
{
    /// <summary>
    /// Validates Narrative structure.
    /// </summary>
    /// <param name="element">The element to validate.</param>
    /// <param name="settings">Validation settings.</param>
    /// <param name="state">Current validation state.</param>
    /// <returns>A validation result indicating success or failure.</returns>
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        var textChildren = element.Children("text");
        if (textChildren.Count == 0)
        {
            return ValidationResult.Success(); // No narrative present (optional)
        }

        var textNode = textChildren[0];
        var issues = new List<ValidationIssue>();

        // Check for status field (required if text present)
        var statusChildren = textNode.Children("status");
        if (statusChildren.Count == 0)
        {
            issues.Add(ValidationIssue.InvariantFailure(
                "txt-1",
                "Narrative must have a status field",
                $"{textNode.Location}.status"));
            return ValidationResult.Failure(issues);
        }

        var statusNode = statusChildren[0];

        string? status = statusNode.Value?.ToString();
        if (status is not ("generated" or "extensions" or "additional" or "empty"))
        {
            issues.Add(ValidationIssue.InvariantFailure(
                "txt-2",
                $"Invalid narrative status: '{status}'. Must be one of: generated, extensions, additional, empty",
                statusNode.Location));
        }

        // Check for div field (required if status is not 'empty')
        if (status != "empty" && !textNode.Children("div").Any())
        {
            issues.Add(ValidationIssue.InvariantFailure(
                "txt-1",
                "Narrative must have a div field when status is not 'empty'",
                $"{textNode.Location}.div"));
        }

        if (issues.Count > 0)
        {
            return ValidationResult.Failure(issues);
        }

        return ValidationResult.Success();
    }
}
