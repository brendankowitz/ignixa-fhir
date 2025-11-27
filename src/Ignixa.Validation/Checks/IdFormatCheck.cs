// <copyright file="IdFormatCheck.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.RegularExpressions;
using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates FHIR resource ID format.
/// Per FHIR spec: [A-Za-z0-9\-\.]{1,64}
/// Tier 1 (Fast) validator.
/// </summary>
public class IdFormatCheck : IValidationCheck
{
    private static readonly Regex IdPattern = new(@"^[A-Za-z0-9\-\.]{1,64}$", RegexOptions.Compiled);

    /// <summary>
    /// Validates that resource ID matches FHIR format requirements.
    /// </summary>
    /// <param name="element">The element to validate.</param>
    /// <param name="settings">Validation settings.</param>
    /// <param name="state">Current validation state.</param>
    /// <returns>A validation result indicating success or failure.</returns>
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        var idChildren = element.Children("id");
        if (idChildren.Count == 0)
        {
            return ValidationResult.Success(); // ID is optional at this level
        }

        var idNode = idChildren[0];

        string? id = idNode.Value?.ToString();
        if (string.IsNullOrEmpty(id))
        {
            return ValidationResult.Success(); // Empty is handled by CardinalityCheck if required
        }

        if (!IdPattern.IsMatch(id))
        {
            return ValidationResult.Failure(
                ValidationIssue.InvariantFailure(
                    "id-1",
                    $"Resource ID '{id}' is not valid. Must match pattern: [A-Za-z0-9\\-\\.]{{1,64}}",
                    idNode.Location));
        }

        return ValidationResult.Success();
    }
}
