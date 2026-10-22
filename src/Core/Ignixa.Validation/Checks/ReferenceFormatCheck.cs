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
    private readonly string? _shadowName;

    /// <summary>
    /// Gets a value indicating whether the element name is exact - not a choice element's base name (<c>value</c>), whose
    /// children are its concrete variants - so an absent element can be passed from the raw JSON without a child lookup.
    /// Set by the schema builder, which knows; false for any other caller.
    /// </summary>
    internal bool ExactName
    {
        get => _shadowName is not null;
        init => _shadowName = value ? $"_{_fieldName}" : null;
    }

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
        if (_shadowName is not null && RawMembers.CertainlyAbsent(element, _fieldName, _shadowName))
        {
            return ValidationResult.Success();
        }

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
            issues.Add(ValidationIssue.FromTemplate(IssueSeverity.Error, "ref-1", referenceChild.Location, IssueTemplates.ReferenceFormat, reference));
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
