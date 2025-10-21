// <copyright file="StructureDefinitionSchemaBuilder.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.FhirPath;
using Ignixa.SourceNodeSerialization.Abstractions;
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
    /// <param name="terminologyService">Optional terminology service for binding validation. If null, binding checks are not created.</param>
    /// <returns>A ValidationSchema with checks derived from the StructureDefinition metadata.</returns>
    /// <exception cref="ArgumentNullException">Thrown if summary is null.</exception>
    public ValidationSchema BuildSchema(
        IStructureDefinitionSummary summary,
        IStructureDefinitionSummaryProvider provider,
        ITerminologyService? terminologyService = null)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(provider);

        var elements = summary.GetElements();

        // Tier 1 (Fast): Universal checks - always run regardless of tier
        var universalChecks = new List<IValidationCheck>
        {
            new JsonStructureCheck(),
            new IdFormatCheck(),
            new NarrativeCheck()
        };

        // Tier 2 (Spec): Schema-driven checks from StructureDefinition
        var specChecks = new List<IValidationCheck>();

        // Extract required field checks
        var requiredChecks = elements
            .Where(e => e.IsRequired)
            .Select(e => new RequiredFieldCheck(e.ElementName, isRequired: true));
        specChecks.AddRange(requiredChecks);

        // Extract cardinality checks
        // Use explicit Min/Max from IExtendedElementMetadata if available, otherwise infer from IsRequired/IsCollection
        var cardinalityChecks = elements
            .Select(e =>
            {
                // Try to get explicit cardinality from extended metadata
                if (e is IExtendedElementMetadata extended)
                {
                    int min = extended.Min ?? (e.IsRequired ? 1 : 0);
                    int? max = extended.Max == "*" ? null : (extended.Max != null ? int.Parse(extended.Max) : (e.IsCollection ? (int?)null : 1));
                    return new CardinalityCheck(e.ElementName, min, max);
                }

                // Fallback to inferred cardinality
                return new CardinalityCheck(
                    e.ElementName,
                    min: e.IsRequired ? 1 : 0,
                    max: e.IsCollection ? (int?)null : 1);
            });
        specChecks.AddRange(cardinalityChecks);

        // Extract type checks (only for primitive types)
        var typeChecks = elements
            .Where(e => !string.IsNullOrEmpty(e.DefaultTypeName))
            .Where(e => IsPrimitiveType(e.DefaultTypeName!))
            .Select(e => new TypeCheck(e.ElementName, e.DefaultTypeName!));
        specChecks.AddRange(typeChecks);

        // Extract reference format checks
        var referenceChecks = elements
            .Where(e => e.DefaultTypeName == "Reference")
            .Select(e => new ReferenceFormatCheck(e.ElementName));
        specChecks.AddRange(referenceChecks);

        // Extract coding structure checks
        var codingChecks = elements
            .Where(e => e.DefaultTypeName is "CodeableConcept" or "Coding")
            .Select(e => new CodingStructureCheck(e.ElementName));
        specChecks.AddRange(codingChecks);

        // Extract choice element checks (value[x] pattern)
        var choiceChecks = elements
            .Where(e => e.IsChoiceElement)
            .Select(e =>
            {
                // Extract base name (remove [x] suffix if present)
                var baseName = e.ElementName.EndsWith("[x]", StringComparison.Ordinal)
                    ? e.ElementName.Substring(0, e.ElementName.Length - 3)
                    : e.ElementName;

                // Get allowed types from Type array
                var allowedTypes = e.Type?
                    .Select(t => t.GetTypeName())
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToArray() ?? Array.Empty<string>();

                return new ChoiceElementCheck(baseName, allowedTypes);
            });
        specChecks.AddRange(choiceChecks);

        // Extract extension structure checks
        var extensionChecks = elements
            .Where(e => e.DefaultTypeName == "Extension")
            .Select(e => new ExtensionStructureCheck(e.ElementName));
        specChecks.AddRange(extensionChecks);

        // Extract fixed value checks from IExtendedElementMetadata
        var fixedValueChecks = elements
            .Where(e => e is IExtendedElementMetadata extended && !string.IsNullOrEmpty(extended.FixedValue))
            .Select(e =>
            {
                var extended = (IExtendedElementMetadata)e;
                return new FixedValueCheck(e.ElementName, extended.FixedValue!);
            });
        specChecks.AddRange(fixedValueChecks);

        // Extract pattern checks from IExtendedElementMetadata
        var patternChecks = elements
            .Where(e => e is IExtendedElementMetadata extended && !string.IsNullOrEmpty(extended.PatternValue))
            .Select(e =>
            {
                var extended = (IExtendedElementMetadata)e;
                return new PatternCheck(e.ElementName, extended.PatternValue!);
            });
        specChecks.AddRange(patternChecks);

        // Extract binding checks from IExtendedElementMetadata (only if terminology service is provided)
        if (terminologyService != null)
        {
            var bindingChecks = elements
                .Where(e => e is IExtendedElementMetadata extended && extended.Binding != null)
                .Select(e =>
                {
                    var extended = (IExtendedElementMetadata)e;
                    var binding = extended.Binding!;
                    return new BindingCheck(
                        e.ElementName,
                        binding.ValueSetUrl,
                        binding.Strength,
                        terminologyService);
                });
            specChecks.AddRange(bindingChecks);
        }

        // Extract unknown property check (only first-level elements)
        var allPropertyNames = elements
            .Select(e => e.ElementName)
            .Where(name => !string.IsNullOrEmpty(name) && !name.Contains('.', StringComparison.Ordinal))
            .ToArray();
        specChecks.Add(new UnknownPropertyCheck(allPropertyNames));

        // Tier 3 (Profile): Advanced checks - FHIRPath invariants, slicing, advanced terminology
        var profileChecks = new List<IValidationCheck>();

        // Extract FHIRPath invariant checks from IExtendedElementMetadata
        // This includes constraints like ele-1, dom-1, resource-specific invariants
        // Moved to Profile tier to avoid false positives on minimal resources
        var invariantChecks = ExtractInvariantChecks(elements.ToArray(), provider, _compiler);
        profileChecks.AddRange(invariantChecks);

        // Build the canonical URL from the type name
        var canonicalUrl = $"http://hl7.org/fhir/StructureDefinition/{summary.TypeName}";

        return new ValidationSchema(
            canonicalUrl: canonicalUrl,
            resourceType: summary.TypeName,
            universalChecks: universalChecks,
            specChecks: specChecks,
            profileChecks: profileChecks);
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
