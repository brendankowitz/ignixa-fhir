// <copyright file="FixedValueCheck.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates that an element has a fixed value as specified in the StructureDefinition.
/// Fixed values must match exactly (deep equality).
/// Tier 2 validator - used in Spec validation tier.
/// </summary>
/// <remarks>
/// FHIR StructureDefinitions can specify a fixed value for an element.
/// For example, an extension.url might be fixed to "http://example.org/ext".
/// This validator ensures that if the element is present, its value matches exactly.
/// </remarks>
public class FixedValueCheck : IValidationCheck
{
    private readonly string _elementPath;
    private readonly JsonNode _fixedValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="FixedValueCheck"/> class.
    /// </summary>
    /// <param name="elementPath">The element path to validate (e.g., "extension.url").</param>
    /// <param name="fixedValueJson">The fixed value as JSON string.</param>
    public FixedValueCheck(string elementPath, string fixedValueJson)
    {
        _elementPath = elementPath ?? throw new ArgumentNullException(nameof(elementPath));

        if (string.IsNullOrWhiteSpace(fixedValueJson))
        {
            throw new ArgumentException("Fixed value JSON cannot be null or whitespace.", nameof(fixedValueJson));
        }

        // Parse the fixed value JSON
        try
        {
            _fixedValue = JsonNode.Parse(fixedValueJson)
                ?? throw new ArgumentException("Fixed value JSON parsed to null.", nameof(fixedValueJson));
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Invalid fixed value JSON: {ex.Message}", nameof(fixedValueJson), ex);
        }
    }

    /// <summary>
    /// Validates that the element's value matches the fixed value.
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
                // Intermediate path has multiple elements - validate each
                var results = new List<ValidationResult>();
                foreach (var child in children)
                {
                    // Continue navigation from this child
                    var remainingPath = string.Join(".", pathParts.Skip(Array.IndexOf(pathParts, part) + 1));
                    var tempCheck = new FixedValueCheck(remainingPath, _fixedValue.ToJsonString());
                    results.Add(tempCheck.Validate(child, settings, state));
                }
                return ValidationResult.Combine(results);
            }

            currentNode = children[0];
        }

        // Validate the target element(s)
        if (targetNodes == null || targetNodes.Count == 0)
        {
            return ValidationResult.Success();
        }

        // Check if fixed value is an array (FHIR elements can be arrays)
        var isFixedArray = _fixedValue is JsonArray;

        // If fixed value is an array OR we have multiple target nodes, build array for comparison
        if (isFixedArray || targetNodes.Count > 1)
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

            // Compare with fixed value using deep equality
            if (!JsonNode.DeepEquals(arrayValue, _fixedValue))
            {
                var actualJson = arrayValue.ToJsonString();
                var expectedJson = _fixedValue.ToJsonString();

                return ValidationResult.Failure(
                    new ValidationIssue(
                        IssueSeverity.Error,
                        "fixed-value-mismatch",
                        location,
                        $"Element '{_elementPath}' has fixed value '{expectedJson}' but found '{actualJson}'"));
            }

            return ValidationResult.Success();
        }

        // Single element with non-array fixed value - validate it directly
        return ValidateNode(targetNodes[0], location);
    }

    private ValidationResult ValidateNode(ISourceNode targetNode, string location)
    {
        // Get the actual value from the node
        var actualValue = GetNodeValue(targetNode);

        // Compare with fixed value using deep equality
        if (!JsonNode.DeepEquals(actualValue, _fixedValue))
        {
            var actualJson = actualValue?.ToJsonString() ?? "null";
            var expectedJson = _fixedValue.ToJsonString();

            return ValidationResult.Failure(
                new ValidationIssue(
                    IssueSeverity.Error,
                    "fixed-value-mismatch",
                    location,
                    $"Element '{_elementPath}' has fixed value '{expectedJson}' but found '{actualJson}'"));
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
                    array.Add(childValue);
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
                if (obj.TryGetPropertyValue(child.Name, out var existing))
                {
                    if (existing is JsonArray existingArray)
                    {
                        existingArray.Add(childValue);
                    }
                    else
                    {
                        var newArray = new JsonArray { existing, childValue };
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
}
