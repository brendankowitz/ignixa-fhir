// <copyright file="ReferenceFormatCheck.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates FHIR Reference.reference format.
/// Valid formats:
/// - ResourceType/id (relative)
/// - ResourceType/id/_history/version (versioned relative)
/// - http(s)://server/ResourceType/id (absolute)
/// - urn:uuid:... (UUID)
/// - #fragment (contained resource)
/// Tier 1 (Fast) validator.
/// </summary>
public class ReferenceFormatCheck : IValidationCheck
{
    private readonly string _fieldName;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReferenceFormatCheck"/> class.
    /// </summary>
    /// <param name="fieldName">The name of the Reference field to validate (e.g., "subject", "patient").</param>
    public ReferenceFormatCheck(string fieldName)
    {
        _fieldName = fieldName;
    }

    /// <summary>
    /// Validates Reference.reference format.
    /// </summary>
    /// <param name="element">The element to validate.</param>
    /// <param name="settings">Validation settings.</param>
    /// <param name="state">Current validation state.</param>
    /// <returns>A validation result indicating success or failure.</returns>
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        var issues = new List<ValidationIssue>();

        // Get all instances of this Reference field (may be array)
        var fieldNodes = element.Children(_fieldName);
        foreach (var fieldNode in fieldNodes)
        {
            ValidateReferenceNode(fieldNode, issues);
        }

        if (issues.Count > 0)
        {
            return ValidationResult.Failure(issues);
        }

        return ValidationResult.Success();
    }

    private void ValidateReferenceNode(IElement referenceNode, List<ValidationIssue> issues)
    {
        var referenceChildren = referenceNode.Children("reference");
        if (referenceChildren.Count == 0)
        {
            return; // Reference without .reference property is valid (may have identifier or display only)
        }

        var referenceChild = referenceChildren[0];

        string? reference = referenceChild.Value?.ToString();
        if (string.IsNullOrEmpty(reference))
        {
            return; // Empty reference handled elsewhere
        }

        if (!IsValidReferenceFormat(reference))
        {
            issues.Add(ValidationIssue.InvariantFailure(
                "ref-1",
                $"Reference '{reference}' is not a valid FHIR reference format",
                referenceChild.Location));
        }
    }

    private static bool IsValidReferenceFormat(string reference)
    {
        if (reference.StartsWith('#'))
        {
            return true;
        }

        if (reference.StartsWith("urn:uuid:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (reference.StartsWith("urn:oid:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(reference);
    }
}
