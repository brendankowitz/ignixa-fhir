// <copyright file="NestedComplexTypeCheck.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates nested complex types (BackboneElement, complex datatypes) by recursively
/// applying schemas to array elements or child objects.
/// Tier 2 (Spec) validator - validates structure and cardinality of nested elements.
/// </summary>
/// <remarks>
/// This check enables validation of nested structures like:
/// - AuditEvent.agent[] (BackboneElement array)
/// - Patient.address[] (Address complex datatype)
/// - Patient.contact (ContactPoint complex datatype)
///
/// For each nested element, applies the full schema validation (required fields,
/// cardinality, types, invariants) to ensure nested properties meet FHIR requirements.
/// </remarks>
public class NestedComplexTypeCheck : IValidationCheck
{
    private readonly string _elementName;
    private readonly bool _isCollection;
    private readonly ValidationSchema _nestedSchema;

    /// <summary>
    /// Initializes a new instance of the <see cref="NestedComplexTypeCheck"/> class.
    /// </summary>
    /// <param name="elementName">The name of the element containing nested types (e.g., "agent", "address").</param>
    /// <param name="isCollection">Whether this element is a collection (array).</param>
    /// <param name="nestedSchema">The pre-built validation schema for the nested type.</param>
    /// <exception cref="ArgumentNullException">Thrown if elementName or nestedSchema is null.</exception>
    public NestedComplexTypeCheck(string elementName, bool isCollection, ValidationSchema nestedSchema)
    {
        _elementName = elementName ?? throw new ArgumentNullException(nameof(elementName));
        _isCollection = isCollection;
        _nestedSchema = nestedSchema ?? throw new ArgumentNullException(nameof(nestedSchema));
    }

    /// <summary>
    /// Validates nested complex type elements by applying the nested schema to each element.
    /// </summary>
    /// <param name="node">The source node to validate.</param>
    /// <param name="settings">Validation settings.</param>
    /// <param name="state">Current validation state.</param>
    /// <returns>A validation result indicating success or failure of nested element validation.</returns>
    public ValidationResult Validate(ISourceNode node, ValidationSettings settings, ValidationState state)
    {
        var issues = new List<ValidationIssue>();

        // Get all instances of this element (may be array)
        var elementNodes = node.Children(_elementName).ToList();

        if (!elementNodes.Any())
        {
            // No elements to validate - cardinality checks handle missing/empty collections
            return ValidationResult.Success();
        }

        // If this is a collection, validate each element with index
        if (_isCollection)
        {
            for (int i = 0; i < elementNodes.Count; i++)
            {
                var elementNode = elementNodes[i];

                // Build path: "agent[0]", "agent[1]", etc.
                string elementPath = $"{_elementName}[{i}]";
                var nestedState = state.WithLocation(elementPath);

                // Validate nested element using the nested schema
                var nestedResult = _nestedSchema.Validate(elementNode, settings, nestedState);
                if (!nestedResult.IsValid)
                {
                    issues.AddRange(nestedResult.Issues);
                }
            }
        }
        else
        {
            // Single nested object
            var elementNode = elementNodes[0];
            var nestedState = state.WithLocation(_elementName);

            // Validate nested element using the nested schema
            var nestedResult = _nestedSchema.Validate(elementNode, settings, nestedState);
            if (!nestedResult.IsValid)
            {
                issues.AddRange(nestedResult.Issues);
            }
        }

        if (issues.Count > 0)
        {
            return ValidationResult.Failure(issues);
        }

        return ValidationResult.Success();
    }
}
