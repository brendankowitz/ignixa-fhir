// <copyright file="StructureDefinitionSchemaBuilder.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.FhirPath;
using Ignixa.SourceNodeSerialization.Specification;
using Ignixa.Specification;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;

namespace Ignixa.Validation.Schema;

/// <summary>
/// Builds ValidationSchema objects from IStructureDefinitionSummaryProvider metadata.
/// Automates the creation of validation checks (RequiredField, Cardinality, Type, Reference)
/// from FHIR StructureDefinition metadata.
/// </summary>
public class StructureDefinitionSchemaBuilder
{
    private readonly FhirPathCompiler _compiler;

    /// <summary>
    /// Initializes a new instance of the <see cref="StructureDefinitionSchemaBuilder"/> class.
    /// </summary>
    /// <param name="compiler">Shared FhirPath compiler for parsing constraint expressions. If null, a new instance will be created.</param>
    public StructureDefinitionSchemaBuilder(FhirPathCompiler? compiler = null)
    {
        _compiler = compiler ?? new FhirPathCompiler();
    }

    /// <summary>
    /// Builds a ValidationSchema from a StructureDefinition summary.
    /// </summary>
    /// <param name="summary">The StructureDefinition summary containing element metadata.</param>
    /// <param name="provider">The provider used to resolve type references (currently unused but included for future extensibility).</param>
    /// <returns>A ValidationSchema with checks derived from the StructureDefinition metadata.</returns>
    /// <exception cref="ArgumentNullException">Thrown if summary is null.</exception>
    public ValidationSchema BuildSchema(
        IStructureDefinitionSummary summary,
        IStructureDefinitionSummaryProvider provider)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(provider);

        var elements = summary.GetElements();
        var allChecks = new List<IValidationCheck>();

        // Extract required field checks
        var requiredChecks = elements
            .Where(e => e.IsRequired)
            .Select(e => new RequiredFieldCheck(e.ElementName, isRequired: true));
        allChecks.AddRange(requiredChecks);

        // Extract cardinality checks
        var cardinalityChecks = elements
            .Select(e => new CardinalityCheck(
                e.ElementName,
                min: e.IsRequired ? 1 : 0,
                max: e.IsCollection ? (int?)null : 1));
        allChecks.AddRange(cardinalityChecks);

        // Extract type checks (only for primitive types)
        var typeChecks = elements
            .Where(e => !string.IsNullOrEmpty(e.DefaultTypeName))
            .Where(e => IsPrimitiveType(e.DefaultTypeName!))
            .Select(e => new TypeCheck(e.ElementName, e.DefaultTypeName!));
        allChecks.AddRange(typeChecks);

        // Extract reference format checks
        var referenceChecks = elements
            .Where(e => e.DefaultTypeName == "Reference")
            .Select(e => new ReferenceFormatCheck(e.ElementName));
        allChecks.AddRange(referenceChecks);

        // Extract coding structure checks
        var codingChecks = elements
            .Where(e => e.DefaultTypeName is "CodeableConcept" or "Coding")
            .Select(e => new CodingStructureCheck(e.ElementName));
        allChecks.AddRange(codingChecks);

        // Extract FHIRPath invariant checks from IExtendedElementMetadata
        // This includes constraints like ele-1, dom-1, resource-specific invariants
        var invariantChecks = ExtractInvariantChecks(elements.ToArray(), provider, _compiler);
        allChecks.AddRange(invariantChecks);

        // Build the canonical URL from the type name
        var canonicalUrl = $"http://hl7.org/fhir/StructureDefinition/{summary.TypeName}";

        return new ValidationSchema(
            canonicalUrl: canonicalUrl,
            resourceType: summary.TypeName,
            checks: allChecks.ToList());
    }

    /// <summary>
    /// Extracts FHIRPath invariant checks from element metadata.
    /// Constraints are provided by IExtendedElementMetadata interface.
    /// </summary>
    /// <param name="elements">The element definitions to extract constraints from.</param>
    /// <param name="provider">The structure definition provider for FHIRPath evaluation.</param>
    /// <param name="compiler">The FhirPath compiler for parsing constraint expressions.</param>
    /// <returns>A collection of FhirPathInvariantCheck instances.</returns>
    private static IEnumerable<IValidationCheck> ExtractInvariantChecks(
        IElementDefinitionSummary[] elements,
        IStructureDefinitionSummaryProvider provider,
        FhirPathCompiler compiler)
    {
        var checks = new List<IValidationCheck>();

        // Deduplicate constraints by key to avoid duplicate checks
        // Multiple elements may reference the same constraint (e.g., ele-1 on every element)
        var seenConstraints = new HashSet<string>();

        foreach (var element in elements)
        {
            // Check if this element has extended metadata with constraints
            if (element is not IExtendedElementMetadata extendedMetadata)
            {
                continue;
            }

            var constraints = extendedMetadata.Constraints;
            if (constraints == null || constraints.Length == 0)
            {
                continue;
            }

            foreach (var constraint in constraints)
            {
                // Skip constraints we've already seen
                // FHIRPath invariants are evaluated at the resource root, not per-element
                if (seenConstraints.Contains(constraint.Key))
                {
                    continue;
                }

                seenConstraints.Add(constraint.Key);

                // Create FhirPathInvariantCheck for this constraint
                // Compiler is passed in from builder instance (shared across all checks)
                var check = new FhirPathInvariantCheck(constraint, provider, compiler);
                checks.Add(check);
            }
        }

        return checks;
    }

    /// <summary>
    /// Determines if a FHIR type name represents a primitive type.
    /// </summary>
    /// <param name="typeName">The FHIR type name to check.</param>
    /// <returns>True if the type is a primitive type; otherwise, false.</returns>
    private static bool IsPrimitiveType(string typeName) =>
        typeName switch
        {
            "id" or "string" or "uri" or "url" or "canonical" or
            "date" or "dateTime" or "instant" or "time" or
            "boolean" or "integer" or "decimal" or "positiveInt" or
            "unsignedInt" or "code" or "oid" or "uuid" => true,
            _ => false,
        };
}
