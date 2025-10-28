// <copyright file="ChoiceElementCheck.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Serialization.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates choice type elements (value[x] pattern).
/// Ensures exactly ONE typed variant is present (e.g., valueString OR valueCode, not both).
/// Tier 2 validator - used in Spec validation tier.
/// </summary>
/// <remarks>
/// FHIR choice elements like Observation.value[x] can be represented as:
/// - valueString, valueCode, valueQuantity, valueInteger, etc.
/// This validator ensures that exactly one variant is present and that it's an allowed type.
/// </remarks>
public class ChoiceElementCheck : IValidationCheck
{
    private readonly string _baseElementName; // e.g., "value" (without [x])
    private readonly string[] _allowedTypes;  // e.g., ["string", "Code", "Quantity"]

    /// <summary>
    /// Initializes a new instance of the <see cref="ChoiceElementCheck"/> class.
    /// </summary>
    /// <param name="baseElementName">The base name of the choice element (without [x] suffix).</param>
    /// <param name="allowedTypes">Array of allowed type names (e.g., ["string", "Code", "Quantity"]).</param>
    public ChoiceElementCheck(string baseElementName, string[] allowedTypes)
    {
        _baseElementName = baseElementName ?? throw new ArgumentNullException(nameof(baseElementName));
        _allowedTypes = allowedTypes ?? throw new ArgumentNullException(nameof(allowedTypes));
    }

    /// <summary>
    /// Validates that exactly one choice type variant is present.
    /// </summary>
    /// <param name="node">The source node to validate.</param>
    /// <param name="settings">Validation settings.</param>
    /// <param name="state">Current validation state.</param>
    /// <returns>A validation result indicating success or failure.</returns>
    public ValidationResult Validate(ISourceNode node, ValidationSettings settings, ValidationState state)
    {
        var location = string.IsNullOrEmpty(node.Location)
            ? _baseElementName
            : $"{node.Location}.{_baseElementName}";

        // Find all children that match the choice pattern (base name + type suffix)
        // e.g., "value*" matches "valueString", "valueCode", "valueQuantity"
        var choiceChildren = node.Children($"{_baseElementName}*").ToList();

        // No choice children found
        if (choiceChildren.Count == 0)
        {
            // This is not necessarily an error - cardinality check handles required elements
            // Choice elements with min=0 are optional
            return ValidationResult.Success();
        }

        // Multiple choice variants found (e.g., both valueString and valueCode)
        if (choiceChildren.Count > 1)
        {
            var foundTypes = string.Join(", ", choiceChildren.Select(c => c.Name));
            return ValidationResult.Failure(
                new ValidationIssue(
                    IssueSeverity.Error,
                    "choice-multiple",
                    location,
                    $"Choice element '{_baseElementName}[x]' can only have one type variant, but found multiple: {foundTypes}"));
        }

        // Exactly one choice variant found - validate it's an allowed type
        var actualChild = choiceChildren[0];
        var actualTypeName = actualChild.Name.Substring(_baseElementName.Length); // Extract type suffix (e.g., "String" from "valueString")

        // Normalize type name (first character may be lowercase in element name but uppercase in Type array)
        // e.g., "valueString" → "String", but Type[] contains "string"
        var normalizedActualType = char.ToUpperInvariant(actualTypeName[0]) + actualTypeName.Substring(1);

        // Check if this type is allowed
        var isAllowedType = _allowedTypes.Any(allowedType =>
            string.Equals(allowedType, normalizedActualType, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(allowedType, actualTypeName, StringComparison.OrdinalIgnoreCase));

        if (!isAllowedType)
        {
            var allowedTypesStr = string.Join(", ", _allowedTypes);
            return ValidationResult.Failure(
                new ValidationIssue(
                    IssueSeverity.Error,
                    "choice-invalid-type",
                    location,
                    $"Choice element '{_baseElementName}[x]' has type '{actualTypeName}' which is not in the allowed types: {allowedTypesStr}"));
        }

        return ValidationResult.Success();
    }
}
