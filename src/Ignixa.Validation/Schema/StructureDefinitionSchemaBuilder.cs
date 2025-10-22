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
    /// <param name="provider">The provider used to resolve type references and build nested schemas.</param>
    /// <param name="terminologyService">Optional terminology service for binding validation. If null, binding checks are not created.</param>
    /// <returns>A ValidationSchema with checks derived from the StructureDefinition metadata.</returns>
    /// <exception cref="ArgumentNullException">Thrown if summary or provider is null.</exception>
    public ValidationSchema BuildSchema(
        IStructureDefinitionSummary summary,
        IStructureDefinitionSummaryProvider provider,
        ITerminologyService? terminologyService = null)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(provider);

        var elements = summary.GetElements();

        // Tier 1 (Fast): Universal checks - always run regardless of tier
        // Includes basic cardinality and type checks to align with Microsoft FHIR Server default
        var universalChecks = new List<IValidationCheck>
        {
            new JsonStructureCheck(),
            new NarrativeCheck()
        };

        // Extract cardinality checks (moved to Fast tier for Microsoft FHIR Server alignment)
        // Cardinality checks enforce both minimum (required fields have min=1) and maximum cardinality
        // This eliminates the need for a separate RequiredFieldCheck
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
        universalChecks.AddRange(cardinalityChecks);

        // Extract type checks (only for primitive types, moved to Fast tier)
        // This covers ID format validation and other primitive type checks
        var typeChecks = elements
            .Where(e => !string.IsNullOrEmpty(e.DefaultTypeName))
            .Where(e => IsPrimitiveType(e.DefaultTypeName!))
            .Select(e => new TypeCheck(e.ElementName, e.DefaultTypeName!));
        universalChecks.AddRange(typeChecks);

        // Tier 2 (Spec): Schema-driven checks from StructureDefinition
        var specChecks = new List<IValidationCheck>();

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

        // Extract nested complex type checks (BackboneElement, complex datatypes)
        var nestedTypeChecks = ExtractNestedTypeChecks(elements.ToArray(), summary, provider);
        specChecks.AddRange(nestedTypeChecks);

        // Extract unknown property check (only first-level elements)
        var allPropertyNames = elements
            .Select(e => e.ElementName)
            .Where(name => !string.IsNullOrEmpty(name) && !name.Contains('.', StringComparison.Ordinal))
            .ToArray();

        // Extract choice element base names for proper validation
        // Some StructureDefinitions store choice elements with just the base name (e.g., "value" not "value[x]")
        var choiceElementBases = elements
            .Where(e => e.IsChoiceElement)
            .Select(e => e.ElementName.EndsWith("[x]", StringComparison.Ordinal)
                ? e.ElementName.Substring(0, e.ElementName.Length - 3)
                : e.ElementName)
            .Distinct()
            .ToArray();

        specChecks.Add(new UnknownPropertyCheck(allPropertyNames, choiceElementBases));

        // Tier 3 (Profile): Advanced checks - FHIRPath invariants, slicing, advanced terminology
        var profileChecks = new List<IValidationCheck>();

        // Extract FHIRPath invariant checks from IExtendedElementMetadata
        // This includes constraints like ele-1, dom-1, resource-specific invariants
        // Moved to Profile tier to avoid false positives on minimal resources
        // Constraints are scoped to the current resource type (see ExtractInvariantChecks for filtering)
        var invariantChecks = ExtractInvariantChecks(elements.ToArray(), summary, provider, _compiler);
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
    /// Only includes constraints that apply to the resource type being validated.
    /// </summary>
    /// <param name="elements">The element definitions to extract constraints from.</param>
    /// <param name="summary">The structure definition being built (for scoping constraints to correct resource type).</param>
    /// <param name="provider">The structure definition provider for FHIRPath evaluation.</param>
    /// <param name="compiler">The FhirPath compiler for parsing constraint expressions.</param>
    /// <returns>A collection of FhirPathInvariantCheck instances.</returns>
    private static IEnumerable<IValidationCheck> ExtractInvariantChecks(
        IElementDefinitionSummary[] elements,
        IStructureDefinitionSummary summary,
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

                // ✅ Filter constraints by AppliesTo scope
                // Only include constraints that either:
                // - Apply to all resources/types (AppliesTo is empty), OR
                // - Explicitly apply to this resource type
                // This prevents constraints like ext-1 (Extension-only) from being applied to MedicationRequest
                if (constraint.AppliesTo.Count > 0 && !constraint.AppliesTo.Contains(summary.TypeName))
                {
                    continue; // Constraint doesn't apply to this resource type
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
    /// Extracts nested complex type checks for BackboneElement and complex datatypes.
    /// Recursively builds schemas for nested types and creates validation checks.
    /// </summary>
    /// <param name="elements">The element definitions to extract nested types from.</param>
    /// <param name="summary">The parent StructureDefinition summary (for building nested type names).</param>
    /// <param name="provider">The structure definition provider for resolving nested types.</param>
    /// <returns>A collection of NestedComplexTypeCheck instances.</returns>
    private static IEnumerable<IValidationCheck> ExtractNestedTypeChecks(
        IElementDefinitionSummary[] elements,
        IStructureDefinitionSummary summary,
        IStructureDefinitionSummaryProvider provider)
    {
        var checks = new List<IValidationCheck>();

        foreach (var element in elements)
        {
            // Skip if not a complex type
            var typeName = element.DefaultTypeName;
            if (string.IsNullOrEmpty(typeName) || IsPrimitiveType(typeName))
            {
                continue;
            }

            // Skip special types that have dedicated checks
            if (typeName is "Reference" or "CodeableConcept" or "Coding" or "Extension")
            {
                continue;
            }

            // Determine the nested type name
            string nestedTypeName;
            if (typeName == "BackboneElement")
            {
                // BackboneElement: ResourceType.ElementName (e.g., "AuditEvent.Agent")
                nestedTypeName = $"{summary.TypeName}.{CapitalizeFirst(element.ElementName)}";
            }
            else
            {
                // Complex datatype: Use as-is (e.g., "Address", "HumanName")
                nestedTypeName = typeName;
            }

            // Try to get the nested type schema
            var nestedSummary = provider.Provide(nestedTypeName);
            if (nestedSummary == null)
            {
                // Nested type not found - may be older FHIR version or unsupported type
                // Skip silently to avoid breaking existing validations
                continue;
            }

            // Build the nested schema
            var nestedBuilder = new StructureDefinitionSchemaBuilder();
            var nestedSchema = nestedBuilder.BuildSchema(nestedSummary, provider);

            // Create the nested type check
            var check = new NestedComplexTypeCheck(element.ElementName, element.IsCollection, nestedSchema);
            checks.Add(check);
        }

        return checks;
    }

    /// <summary>
    /// Capitalizes the first character of a string.
    /// </summary>
    /// <param name="str">The string to capitalize.</param>
    /// <returns>The capitalized string, or original if empty.</returns>
    private static string CapitalizeFirst(string str)
    {
        if (string.IsNullOrEmpty(str) || char.IsUpper(str[0]))
        {
            return str;
        }

        return char.ToUpperInvariant(str[0]) + str.Substring(1);
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
