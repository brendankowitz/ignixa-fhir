// <copyright file="ExtensionStructureCheck.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Serialization.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates basic FHIR extension structure.
/// Tier 2 validator - used in Spec validation tier.
/// </summary>
/// <remarks>
/// FHIR extensions must follow specific rules:
/// 1. MUST have a 'url' property (identifies the extension)
/// 2. MUST have either:
///    - A value[x] property (simple extension), OR
///    - Nested 'extension' array (complex extension)
/// 3. MUST NOT have both value[x] and nested extensions
/// </remarks>
public class ExtensionStructureCheck : IValidationCheck
{
    private readonly string _elementName;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtensionStructureCheck"/> class.
    /// </summary>
    /// <param name="elementName">The name of the extension element to validate.</param>
    public ExtensionStructureCheck(string elementName)
    {
        _elementName = elementName ?? throw new ArgumentNullException(nameof(elementName));
    }

    /// <summary>
    /// Validates extension structure.
    /// </summary>
    /// <param name="node">The source node to validate.</param>
    /// <param name="settings">Validation settings.</param>
    /// <param name="state">Current validation state.</param>
    /// <returns>A validation result indicating success or failure.</returns>
    public ValidationResult Validate(ISourceNode node, ValidationSettings settings, ValidationState state)
    {
        var location = string.IsNullOrEmpty(node.Location)
            ? _elementName
            : $"{node.Location}.{_elementName}";

        // Get extension elements
        var extensions = node.Children(_elementName).ToList();

        // No extensions found - this is valid (extensions are optional)
        if (extensions.Count == 0)
        {
            return ValidationResult.Success();
        }

        // Validate each extension
        var issues = new List<ValidationIssue>();

        foreach (var extension in extensions)
        {
            var extensionLocation = extension.Location ?? location;

            // Rule 1: Extension MUST have 'url' property
            var urlChild = extension.Children("url").FirstOrDefault();
            if (urlChild == null)
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    "ext-url-required",
                    extensionLocation,
                    "Extension must have a 'url' property"));
                continue; // Skip further validation for this extension
            }

            // Rule 2: Extension MUST have either value[x] OR nested extensions
            var valueChildren = extension.Children("value*").ToList(); // Matches valueString, valueCode, etc.
            var nestedExtensions = extension.Children("extension").ToList();

            bool hasValue = valueChildren.Any();
            bool hasNestedExtensions = nestedExtensions.Any();

            // Neither value nor nested extensions
            if (!hasValue && !hasNestedExtensions)
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    "ext-content-required",
                    extensionLocation,
                    "Extension must have either a value[x] property or nested 'extension' array"));
            }

            // Rule 3: MUST NOT have both value and nested extensions
            if (hasValue && hasNestedExtensions)
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    "ext-both-value-and-nested",
                    extensionLocation,
                    "Extension cannot have both a value[x] property and nested 'extension' array - choose one"));
            }
        }

        return issues.Any()
            ? ValidationResult.Failure(issues.ToArray())
            : ValidationResult.Success();
    }
}
