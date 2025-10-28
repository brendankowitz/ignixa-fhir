// <copyright file="PatternCheck.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Serialization.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates that an element matches a pattern value as specified in the StructureDefinition.
/// Pattern values allow partial matching - the actual value must contain all properties from the pattern,
/// but can have additional properties.
/// Tier 2 validator - used in Spec validation tier.
/// </summary>
/// <remarks>
/// FHIR StructureDefinitions can specify a pattern value for an element.
/// Unlike fixed values (which require exact match), patterns allow additional properties.
/// For example, a CodeableConcept pattern might require coding.system = "http://loinc.org",
/// but allows additional codings, text, and other properties.
/// </remarks>
public class PatternCheck : IValidationCheck
{
    private readonly string _elementPath;
    private readonly JsonNode _patternValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="PatternCheck"/> class.
    /// </summary>
    /// <param name="elementPath">The element path to validate (e.g., "code").</param>
    /// <param name="patternValueJson">The pattern value as JSON string.</param>
    public PatternCheck(string elementPath, string patternValueJson)
    {
        _elementPath = elementPath ?? throw new ArgumentNullException(nameof(elementPath));

        if (string.IsNullOrWhiteSpace(patternValueJson))
        {
            throw new ArgumentException("Pattern value JSON cannot be null or whitespace.", nameof(patternValueJson));
        }

        // Parse the pattern value JSON
        try
        {
            _patternValue = JsonNode.Parse(patternValueJson)
                ?? throw new ArgumentException("Pattern value JSON parsed to null.", nameof(patternValueJson));
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Invalid pattern value JSON: {ex.Message}", nameof(patternValueJson), ex);
        }
    }

    /// <summary>
    /// Validates that the element's value contains all properties from the pattern.
    /// </summary>
    /// <param name="node">The source node to validate.</param>
    /// <param name="settings">Validation settings.</param>
    /// <param name="state">Current validation state.</param>
    /// <returns>A validation result indicating success or failure.</returns>
    public ValidationResult Validate(ISourceNode node, ValidationSettings settings, ValidationState state)
    {
        var location = string.IsNullOrEmpty(node.Location)
            ? _elementPath
            : $"{node.Location}.{_elementPath}";

        // Navigate to the element using the path
        var pathParts = _elementPath.Split('.');
        ISourceNode? currentNode = node;
        List<ISourceNode>? targetNodes = null;

        foreach (var part in pathParts)
        {
            var children = currentNode.Children(part).ToList();

            // No children found - element is optional or validated by cardinality check
            if (children.Count == 0)
            {
                return ValidationResult.Success();
            }

            // Check if this is the last part of the path
            var isLastPart = part == pathParts[^1];

            if (isLastPart)
            {
                // Save all children for validation (handles arrays)
                targetNodes = children;
                break;
            }

            // For intermediate navigation, only one child expected
            if (children.Count > 1)
            {
                // Intermediate path has multiple elements - this is unexpected
                // Just take the first one
                currentNode = children[0];
            }
            else
            {
                currentNode = children[0];
            }
        }

        // Validate the target element(s)
        if (targetNodes == null || targetNodes.Count == 0)
        {
            return ValidationResult.Success();
        }

        // Check if pattern is an array (FHIR elements can be arrays)
        var isPatternArray = _patternValue is JsonArray;

        // If pattern is an array OR we have multiple target nodes, build array for comparison
        if (isPatternArray || targetNodes.Count > 1)
        {
            var arrayValue = new JsonArray();
            foreach (var targetNode in targetNodes)
            {
                var nodeValue = GetNodeValue(targetNode);
                if (nodeValue != null)
                {
                    arrayValue.Add(nodeValue);
                }
            }

            // Validate the array against the pattern
            if (!MatchesPattern(arrayValue, _patternValue, location, out var errorMessage))
            {
                return ValidationResult.Failure(
                    new ValidationIssue(
                        IssueSeverity.Error,
                        "pattern-mismatch",
                        location,
                        errorMessage ?? $"Element '{_elementPath}' does not match the required pattern"));
            }

            return ValidationResult.Success();
        }

        // Single element with non-array pattern - validate it directly
        return ValidateNode(targetNodes[0], location);
    }

    private ValidationResult ValidateNode(ISourceNode targetNode, string location)
    {
        // Get the actual value from the node
        var actualValue = GetNodeValue(targetNode);

        // Check if actual value contains all properties from pattern
        if (!MatchesPattern(actualValue, _patternValue, location, out var errorMessage))
        {
            return ValidationResult.Failure(
                new ValidationIssue(
                    IssueSeverity.Error,
                    "pattern-mismatch",
                    location,
                    errorMessage ?? $"Element '{_elementPath}' does not match the required pattern"));
        }

        return ValidationResult.Success();
    }

    private static JsonNode? GetNodeValue(ISourceNode node)
    {
        // For primitive types, get the value directly
        var primitiveValue = node.Text;
        if (!string.IsNullOrEmpty(primitiveValue))
        {
            // Try to parse as appropriate type
            if (bool.TryParse(primitiveValue, out var boolValue))
            {
                return JsonValue.Create(boolValue);
            }
            if (int.TryParse(primitiveValue, out var intValue))
            {
                return JsonValue.Create(intValue);
            }
            if (decimal.TryParse(primitiveValue, out var decimalValue))
            {
                return JsonValue.Create(decimalValue);
            }

            // Default to string
            return JsonValue.Create(primitiveValue);
        }

        // For complex types, build a JsonObject or JsonArray from children
        var children = node.Children().ToList();
        if (children.Count == 0)
        {
            return null;
        }

        // Check if all children have the same name (indicates an array)
        var firstChildName = children[0].Name;
        if (children.All(c => c.Name == firstChildName))
        {
            // This is an array - build JsonArray
            var array = new JsonArray();
            foreach (var child in children)
            {
                var childValue = GetNodeValue(child);
                if (childValue != null)
                {
                    // Clone the value to avoid "node already has a parent" error
                    var clonedValue = JsonNode.Parse(childValue.ToJsonString())!;
                    array.Add(clonedValue);
                }
            }
            return array;
        }

        // This is an object - build JsonObject
        var obj = new JsonObject();
        foreach (var child in children)
        {
            var childValue = GetNodeValue(child);
            if (childValue != null)
            {
                // If property already exists, convert to array
                if (obj.TryGetPropertyValue(child.Name, out var existing) && existing != null)
                {
                    if (existing is JsonArray existingArray)
                    {
                        // Clone the childValue to avoid "node already has a parent" error
                        var clonedValue = JsonNode.Parse(childValue.ToJsonString())!;
                        existingArray.Add(clonedValue);
                    }
                    else
                    {
                        // Clone values to avoid "node already has a parent" error
                        var existingClone = JsonNode.Parse(existing.ToJsonString())!;
                        var childClone = JsonNode.Parse(childValue.ToJsonString())!;
                        var newArray = new JsonArray { existingClone, childClone };
                        obj[child.Name] = newArray;
                    }
                }
                else
                {
                    obj[child.Name] = childValue;
                }
            }
        }

        return obj;
    }

    private static bool MatchesPattern(JsonNode? actualValue, JsonNode patternValue, string location, out string? errorMessage)
    {
        errorMessage = null;

        // Null actual value doesn't match any pattern
        if (actualValue == null)
        {
            errorMessage = $"Element at '{location}' is null but pattern requires a value";
            return false;
        }

        // For primitive values (JsonValue), must match exactly
        if (patternValue is JsonValue)
        {
            if (!JsonNode.DeepEquals(actualValue, patternValue))
            {
                errorMessage = $"Element at '{location}' has value '{actualValue.ToJsonString()}' but pattern requires '{patternValue.ToJsonString()}'";
                return false;
            }
            return true;
        }

        // For arrays, check that actual contains all pattern elements
        if (patternValue is JsonArray patternArray)
        {
            if (actualValue is not JsonArray actualArray)
            {
                errorMessage = $"Element at '{location}' is not an array but pattern requires an array";
                return false;
            }

            // For each pattern element, find a matching element in actual
            foreach (var patternElement in patternArray)
            {
                if (patternElement == null)
                {
                    continue;
                }

                var found = false;
                string? lastErrorMessage = null;
                foreach (var actualElement in actualArray)
                {
                    if (actualElement == null)
                    {
                        continue;
                    }

                    if (MatchesPattern(actualElement, patternElement, location, out string? elementError))
                    {
                        found = true;
                        break;
                    }

                    // Save the error message from this attempt
                    lastErrorMessage = elementError;
                }

                if (!found)
                {
                    // Use the more specific error message if available
                    errorMessage = lastErrorMessage ?? $"Element at '{location}' does not contain required pattern element '{patternElement.ToJsonString()}'";
                    return false;
                }
            }

            return true;
        }

        // For objects, check that actual contains all pattern properties
        if (patternValue is JsonObject patternObj)
        {
            if (actualValue is not JsonObject actualObj)
            {
                errorMessage = $"Element at '{location}' is not an object but pattern requires an object";
                return false;
            }

            foreach (var patternProperty in patternObj)
            {
                var propertyName = patternProperty.Key;
                var patternPropertyValue = patternProperty.Value;

                if (patternPropertyValue == null)
                {
                    continue;
                }

                // Check if actual has this property
                if (!actualObj.TryGetPropertyValue(propertyName, out var actualPropertyValue) || actualPropertyValue == null)
                {
                    errorMessage = $"Element at '{location}' is missing required property '{propertyName}' from pattern";
                    return false;
                }

                // Recursively check that the property value matches
                if (!MatchesPattern(actualPropertyValue, patternPropertyValue, $"{location}.{propertyName}", out errorMessage))
                {
                    return false;
                }
            }

            return true;
        }

        // Unknown node type
        errorMessage = $"Unknown pattern type at '{location}'";
        return false;
    }
}
