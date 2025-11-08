// <copyright file="UnknownPropertyCheck.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates that all properties in a resource are defined in the StructureDefinition.
/// Detects unexpected/unknown properties that are not part of the FHIR schema.
/// Tier 2 validator - used in Spec validation tier.
/// </summary>
/// <remarks>
/// FHIR resources must only contain properties defined in their StructureDefinition.
/// Exceptions:
/// - Shadow properties (_propertyName) for primitive extensions
/// - Standard extension arrays (extension, modifierExtension)
/// - Universal resource properties (id, resourceType, meta, implicitRules, language, text, contained)
/// </remarks>
public class UnknownPropertyCheck : IValidationCheck
{
    private readonly HashSet<string> _allowedPropertyNames;
    private readonly HashSet<string> _choiceElementBases; // Base names of choice elements (e.g., "value", "effective")
    private static readonly HashSet<string> UniversalProperties = new(StringComparer.Ordinal)
    {
        "id", "resourceType", "meta", "implicitRules", "language", "text", "contained"
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="UnknownPropertyCheck"/> class.
    /// </summary>
    /// <param name="allowedPropertyNames">The collection of allowed property names from the StructureDefinition.</param>
    /// <param name="choiceElementBases">Optional: explicitly provided choice element base names (e.g., "value", "effective").</param>
    public UnknownPropertyCheck(IEnumerable<string> allowedPropertyNames, IEnumerable<string>? choiceElementBases = null)
    {
        _allowedPropertyNames = new HashSet<string>(allowedPropertyNames, StringComparer.Ordinal);

        // Use provided choice element bases, or extract from property names
        if (choiceElementBases != null)
        {
            _choiceElementBases = new HashSet<string>(choiceElementBases, StringComparer.Ordinal);
        }
        else
        {
            // Extract choice element base names (properties ending with [x])
            _choiceElementBases = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in allowedPropertyNames)
            {
                if (name.EndsWith("[x]", StringComparison.Ordinal))
                {
                    // Extract base name (e.g., "value[x]" → "value")
                    var baseName = name.Substring(0, name.Length - 3);
                    _choiceElementBases.Add(baseName);
                }
            }
        }
    }

    /// <summary>
    /// Validates that all properties in the node are defined in the schema.
    /// </summary>
    /// <param name="node">The source node to validate.</param>
    /// <param name="settings">Validation settings.</param>
    /// <param name="state">Current validation state.</param>
    /// <returns>A validation result indicating success or failure.</returns>
    public ValidationResult Validate(ISourceNode node, ValidationSettings settings, ValidationState state)
    {
        var issues = new List<ValidationIssue>();
        var location = node.Location ?? "Resource";

        // Get all actual properties in the resource
        var actualProperties = GetAllPropertyNames(node);

        foreach (var property in actualProperties)
        {
            // Skip universal properties
            if (UniversalProperties.Contains(property))
                continue;

            // Skip shadow properties (start with _)
            if (property.StartsWith("_", StringComparison.Ordinal))
            {
                // Verify the corresponding main property exists or is allowed
                var mainProperty = property.Substring(1);
                if (_allowedPropertyNames.Contains(mainProperty) || IsChoiceTypeProperty(mainProperty))
                    continue;
            }

            // Skip standard extension arrays
            if (property == "extension" || property == "modifierExtension")
                continue;

            // Check if property is in allowed list
            if (!_allowedPropertyNames.Contains(property))
            {
                // Check if this might be a choice type property (e.g., "valueQuantity" where "value[x]" is allowed)
                if (!IsChoiceTypeProperty(property))
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Error,
                        "unknown-property",
                        $"{location}.{property}",
                        $"Property '{property}' is not defined in the FHIR StructureDefinition for this resource type"));
                }
            }
        }

        return issues.Any()
            ? ValidationResult.Failure(issues.ToArray())
            : ValidationResult.Success();
    }

    /// <summary>
    /// Checks if a property name matches a choice type pattern.
    /// </summary>
    /// <param name="propertyName">The property name to check (e.g., "valueQuantity", "effectiveDateTime").</param>
    /// <returns>True if the property matches a choice type base name; otherwise, false.</returns>
    private bool IsChoiceTypeProperty(string propertyName)
    {
        // Check if any choice element base is a prefix of this property
        // e.g., "value" is a prefix of "valueQuantity"
        foreach (var choiceBase in _choiceElementBases)
        {
            if (propertyName.StartsWith(choiceBase, StringComparison.Ordinal) &&
                propertyName.Length > choiceBase.Length)
            {
                // Verify the suffix starts with an uppercase letter (type name convention)
                // e.g., "valueQuantity" → "Q", "effectiveDateTime" → "D"
                var suffix = propertyName.Substring(choiceBase.Length);
                if (suffix.Length > 0 && char.IsUpper(suffix[0]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Gets all property names from the source node.
    /// </summary>
    /// <param name="node">The source node to extract property names from.</param>
    /// <returns>A collection of unique property names.</returns>
    private static IEnumerable<string> GetAllPropertyNames(ISourceNode node)
    {
        // Get all child property names from ISourceNode
        var properties = new HashSet<string>(StringComparer.Ordinal);

        foreach (var child in node.Children())
        {
            if (!string.IsNullOrEmpty(child.Name))
            {
                properties.Add(child.Name);
            }
        }

        return properties;
    }
}
