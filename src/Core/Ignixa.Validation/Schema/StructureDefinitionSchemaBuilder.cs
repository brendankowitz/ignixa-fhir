// <copyright file="StructureDefinitionSchemaBuilder.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.FhirPath;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Parser;
using Ignixa.Specification;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;

namespace Ignixa.Validation.Schema;

/// <summary>
/// Builds ValidationSchema objects from ISchema metadata.
/// Automates the creation of validation checks (RequiredField, Cardinality, Type, Reference)
/// from FHIR StructureDefinition metadata.
/// </summary>
public class StructureDefinitionSchemaBuilder
{
    private readonly FhirPathParser _parser;

    /// <summary>
    /// Initializes a new instance of the <see cref="StructureDefinitionSchemaBuilder"/> class.
    /// </summary>
    /// <param name="compiler">Shared FhirPath compiler for parsing constraint expressions. If null, a new instance will be created.</param>
    public StructureDefinitionSchemaBuilder(FhirPathParser? compiler = null)
    {
        _parser = compiler ?? new FhirPathParser();
    }

    /// <summary>
    /// Builds a ValidationSchema from a type definition.
    /// </summary>
    /// <param name="typeDefinition">The type definition containing element metadata.</param>
    /// <param name="schema">The schema used to resolve type references and build nested schemas.</param>
    /// <param name="terminologyService">Optional terminology service for binding validation. If null, binding checks are not created.</param>
    /// <param name="validResourceTypes">Optional set of valid FHIR resource type names for resourceType validation. If provided, a ResourceTypeValidationCheck is added.</param>
    /// <param name="validationSchemaResolver">Optional validation schema resolver for contained resource validation. If provided, a ContainedResourceCheck is added for resources.</param>
    /// <returns>A ValidationSchema with checks derived from the type definition metadata.</returns>
    /// <exception cref="ArgumentNullException">Thrown if typeDefinition or schema is null.</exception>
    public ValidationSchema BuildSchema(
        IType typeDefinition,
        ISchema schema,
        ITerminologyService? terminologyService = null,
        IReadOnlySet<string>? validResourceTypes = null,
        IValidationSchemaResolver? validationSchemaResolver = null)
    {
        ArgumentNullException.ThrowIfNull(typeDefinition);
        ArgumentNullException.ThrowIfNull(schema);

        var elements = typeDefinition.Children;

        // Tier 1 (Fast): Universal checks - always run regardless of tier
        // Includes basic cardinality and type checks to align with Microsoft FHIR Server default
        var universalChecks = new List<IValidationCheck>();

        // Only add resource-level checks for actual FHIR resources, not BackboneElements or complex datatypes
        // BackboneElements (e.g., AuditEvent.Agent) and complex types (e.g., Address) don't have resourceType
        if (typeDefinition.Info.IsResource)
        {
            universalChecks.Add(new JsonStructureCheck());

            // Add resourceType validation if valid resource types are provided
            if (validResourceTypes is not null && validResourceTypes.Count > 0)
            {
                universalChecks.Add(new ResourceTypeValidationCheck(validResourceTypes));
            }

            universalChecks.Add(new NarrativeCheck());
        }

        // Extract cardinality checks (moved to Fast tier for Microsoft FHIR Server alignment)
        // Cardinality checks enforce both minimum (required fields have min=1) and maximum cardinality
        // This eliminates the need for a separate RequiredFieldCheck
        // Use explicit Min/Max from ITypeExtended if available, otherwise infer from IsRequired/IsCollection
        // IMPORTANT: Skip xhtml elements (e.g., div) - xhtml stores content directly, not in a .value child
        var cardinalityChecks = elements
            .Where(e => GetTypeName(e) != "xhtml") // Skip xhtml elements - they don't have .value children
            .Select(e =>
            {
                // Try to get explicit cardinality from extended metadata
                if (e is ITypeExtended extended)
                {
                    int min = extended.Min;
                    int? max = extended.Max == "*" ? null : int.Parse(extended.Max);
                    return new CardinalityCheck(e.Info.Name, min, max);
                }

                // Fallback to inferred cardinality
                return new CardinalityCheck(
                    e.Info.Name,
                    min: e.IsRequired ? 1 : 0,
                    max: e.IsCollection ? (int?)null : 1);
            });
        universalChecks.AddRange(cardinalityChecks);

        // Extract type checks (only for primitive types, moved to Fast tier)
        // This covers ID format validation and other primitive type checks
        // Use element name as the first parameter, and the actual FHIR type from ITypeExtended
        var typeChecks = elements
            .Where(e => e.Info.IsPrimitive)
            .Select(e => new TypeCheck(e.Info.Name, GetTypeName(e)));
        universalChecks.AddRange(typeChecks);

        // Tier 2 (Spec): Schema-driven checks from StructureDefinition
        var specChecks = new List<IValidationCheck>();

        // Extract reference format checks - check the type name, not element name
        var referenceChecks = elements
            .Where(e => GetTypeName(e) == "Reference")
            .Select(e => new ReferenceFormatCheck(e.Info.Name));
        specChecks.AddRange(referenceChecks);

        // Extract coding structure checks - check the type name, not element name
        var codingChecks = elements
            .Where(e => GetTypeName(e) is "CodeableConcept" or "Coding")
            .Select(e => new CodingStructureCheck(e.Info.Name));
        specChecks.AddRange(codingChecks);

        // Extract choice element checks (value[x] pattern)
        var choiceChecks = elements
            .Where(e => e.Info.IsChoiceElement)
            .Select(e =>
            {
                // Extract base name (remove [x] suffix if present)
                var baseName = e.Info.Name.EndsWith("[x]", StringComparison.Ordinal)
                    ? e.Info.Name.Substring(0, e.Info.Name.Length - 3)
                    : e.Info.Name;

                // Get allowed types from Types property (ITypeExtended)
                string[] allowedTypes;
                if (e is ITypeExtended extended)
                {
                    allowedTypes = extended.Types
                        .Select(t => t.Code)
                        .Where(name => !string.IsNullOrEmpty(name))
                        .ToArray();
                }
                else
                {
                    allowedTypes = Array.Empty<string>();
                }

                return new ChoiceElementCheck(baseName, allowedTypes);
            });
        specChecks.AddRange(choiceChecks);

        // Extract extension structure checks
        var extensionChecks = elements
            .Where(e => e.Info.Name == "Extension")
            .Select(e => new ExtensionStructureCheck(e.Info.Name));
        specChecks.AddRange(extensionChecks);

        // Extract fixed value checks from ITypeExtended
        var fixedValueChecks = elements
            .Where(e => e is ITypeExtended extended && extended.FixedValue != null)
            .Select(e =>
            {
                var extended = (ITypeExtended)e;
                return new FixedValueCheck(e.Info.Name, extended.FixedValue!.ToString()!);
            });
        specChecks.AddRange(fixedValueChecks);

        // Extract pattern checks from ITypeExtended
        var patternChecks = elements
            .Where(e => e is ITypeExtended extended && extended.PatternValue != null)
            .Select(e =>
            {
                var extended = (ITypeExtended)e;
                return new PatternCheck(e.Info.Name, extended.PatternValue!.ToString()!);
            });
        specChecks.AddRange(patternChecks);

        // Extract binding checks from ITypeExtended (only if terminology service is provided)
        if (terminologyService != null)
        {
            var bindingChecks = elements
                .Where(e => e is ITypeExtended extended && extended.Binding != null)
                .Select(e =>
                {
                    var extended = (ITypeExtended)e;
                    var binding = extended.Binding!;
                    return new BindingCheck(
                        e.Info.Name,
                        binding.ValueSet ?? string.Empty,
                        binding.Strength,
                        terminologyService);
                });
            specChecks.AddRange(bindingChecks);
        }

        // Extract nested complex type checks (BackboneElement, complex datatypes)
        var nestedTypeChecks = ExtractNestedTypeChecks(elements, typeDefinition, schema, terminologyService);
        specChecks.AddRange(nestedTypeChecks);

        // Extract unknown property check (only first-level elements)
        var allPropertyNames = elements
            .Select(e => e.Info.Name)
            .Where(name => !string.IsNullOrEmpty(name) && !name.Contains('.', StringComparison.Ordinal))
            .ToArray();

        // Extract choice element base names for proper validation
        // Some StructureDefinitions store choice elements with just the base name (e.g., "value" not "value[x]")
        var choiceElementBases = elements
            .Where(e => e.Info.IsChoiceElement)
            .Select(e => e.Info.Name.EndsWith("[x]", StringComparison.Ordinal)
                ? e.Info.Name.Substring(0, e.Info.Name.Length - 3)
                : e.Info.Name)
            .Distinct()
            .ToArray();

        specChecks.Add(new UnknownPropertyCheck(allPropertyNames, choiceElementBases, typeDefinition.Info.Name));

        // Add contained resource check for resources (requires schema resolver)
        // Contained resources must be validated against their own StructureDefinition, not the parent's
        if (typeDefinition.Info.IsResource && validationSchemaResolver is not null)
        {
            specChecks.Add(new ContainedResourceCheck(validationSchemaResolver));
        }

        // Tier 3 (Profile): Advanced checks - FHIRPath invariants, slicing, advanced terminology
        var profileChecks = new List<IValidationCheck>();

        // Extract FHIRPath invariant checks from ITypeExtended
        // This includes constraints like ele-1, dom-1, resource-specific invariants
        // Moved to Profile tier to avoid false positives on minimal resources
        // Constraints are scoped to the current resource type (see ExtractInvariantChecks for filtering)
        var invariantChecks = ExtractInvariantChecks(elements, typeDefinition, schema, _parser);
        profileChecks.AddRange(invariantChecks);

        // Build the canonical URL from the type name
        var canonicalUrl = $"http://hl7.org/fhir/StructureDefinition/{typeDefinition.Info.Name}";

        return new ValidationSchema(
            canonicalUrl: canonicalUrl,
            resourceType: typeDefinition.Info.Name,
            universalChecks: universalChecks,
            specChecks: specChecks,
            profileChecks: profileChecks);
    }

    /// <summary>
    /// Extracts FHIRPath invariant checks from element metadata.
    /// Constraints are provided by ITypeExtended interface.
    /// Only includes constraints that apply to the resource type being validated.
    /// </summary>
    /// <param name="elements">The element definitions to extract constraints from.</param>
    /// <param name="typeDefinition">The type definition being built (for scoping constraints to correct resource type).</param>
    /// <param name="schema">The schema for FHIRPath evaluation.</param>
    /// <param name="parser">The FhirPath compiler for parsing constraint expressions.</param>
    /// <returns>A collection of FhirPathInvariantCheck instances.</returns>
    private static IEnumerable<IValidationCheck> ExtractInvariantChecks(
        IReadOnlyList<IType> elements,
        IType typeDefinition,
        ISchema schema,
        FhirPathParser parser)
    {
        var checks = new List<IValidationCheck>();

        // Deduplicate constraints by key to avoid duplicate checks
        // Multiple elements may reference the same constraint (e.g., ele-1 on every element)
        var seenConstraints = new HashSet<string>();

        foreach (var element in elements)
        {
            // Check if this element has extended metadata with constraints
            if (element is not ITypeExtended extendedMetadata)
            {
                continue;
            }

            var constraints = extendedMetadata.Constraints;
            if (constraints == null || constraints.Count == 0)
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

                // Cast to concrete ConstraintDefinition to access AppliesTo property
                // The IConstraint interface doesn't have AppliesTo yet, but the concrete implementation does
                // Note: We cast through object because IConstraint (Abstractions) and ConstraintDefinition (Specification)
                // are not directly related in the type hierarchy, but the runtime type from codegen is ConstraintDefinition
                var constraintObj = (object)constraint;
                if (constraintObj is Specification.ConstraintDefinition constraintDef)
                {
                    // ✅ Filter constraints by AppliesTo scope
                    // Only include constraints that either:
                    // - Apply to all resources/types (AppliesTo is empty), OR
                    // - Explicitly apply to this resource type
                    // This prevents constraints like ext-1 (Extension-only) from being applied to MedicationRequest
                    if (constraintDef.AppliesTo.Count > 0 && !constraintDef.AppliesTo.Contains(typeDefinition.Info.Name))
                    {
                        continue; // Constraint doesn't apply to this resource type
                    }

                    seenConstraints.Add(constraint.Key);

                    // Create FhirPathInvariantCheck for this constraint
                    // Compiler is passed in from builder instance (shared across all checks)
                    var check = new FhirPathInvariantCheck(constraintDef, schema, parser);
                    checks.Add(check);
                }
            }
        }

        return checks;
    }

    /// <summary>
    /// Extracts nested complex type checks for BackboneElement and complex datatypes.
    /// Recursively builds schemas for nested types and creates validation checks.
    /// </summary>
    /// <param name="elements">The element definitions to extract nested types from.</param>
    /// <param name="typeDefinition">The parent type definition (for building nested type names).</param>
    /// <param name="schema">The schema for resolving nested types.</param>
    /// <param name="terminologyService">Optional terminology service for binding validation in nested types.</param>
    /// <returns>A collection of NestedComplexTypeCheck instances.</returns>
    private static IEnumerable<IValidationCheck> ExtractNestedTypeChecks(
        IReadOnlyList<IType> elements,
        IType typeDefinition,
        ISchema schema,
        ITerminologyService? terminologyService)
    {
        var checks = new List<IValidationCheck>();

        foreach (var element in elements)
        {
            // Skip if primitive type
            if (element.Info.IsPrimitive)
            {
                continue;
            }

            // Get the type name from extended metadata using GetTypeName helper
            var typeName = GetTypeName(element);

            if (string.IsNullOrEmpty(typeName) || typeName == element.Info.Name)
            {
                // If no type found or type is same as element name (no extended metadata), skip
                // This happens for elements without ITypeExtended metadata
                continue;
            }

            // Skip special types that have dedicated checks
            // Also skip xhtml - it's a primitive that stores content directly, not in child elements
            // Skip "Resource" type - this is used for contained resources, which are handled by ContainedResourceCheck
            if (typeName is "Reference" or "CodeableConcept" or "Coding" or "Extension" or "xhtml" or "Resource")
            {
                continue;
            }

            // Determine the nested type name
            string nestedTypeName;
            if (typeName == "BackboneElement")
            {
                // BackboneElement: ResourceType.ElementName (e.g., "AuditEvent.Agent")
                nestedTypeName = $"{typeDefinition.Info.Name}.{CapitalizeFirst(element.Info.Name)}";
            }
            else
            {
                // Complex datatype: Use as-is (e.g., "Address", "HumanName")
                nestedTypeName = typeName;
            }

            // Try to get the nested type schema
            var nestedTypeDefinition = schema.GetTypeDefinition(nestedTypeName);
            if (nestedTypeDefinition == null)
            {
                // Nested type not found - may be older FHIR version or unsupported type
                // Skip silently to avoid breaking existing validations
                continue;
            }

            // Build the nested schema
            var nestedBuilder = new StructureDefinitionSchemaBuilder();
            var nestedSchema = nestedBuilder.BuildSchema(nestedTypeDefinition, schema, terminologyService);

            // Create the nested type check
            var check = new NestedComplexTypeCheck(element.Info.Name, element.IsCollection, nestedSchema);
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
    /// Gets the FHIR type name from an element definition.
    /// For elements with ITypeExtended, returns DefaultTypeName or Types[0].Code.
    /// Falls back to Info.Name for elements without extended metadata.
    /// </summary>
    /// <param name="element">The element to get the type name from.</param>
    /// <returns>The FHIR type name.</returns>
    private static string GetTypeName(IType element)
    {
        if (element is ITypeExtended extended)
        {
            // Use DefaultTypeName if available
            if (!string.IsNullOrEmpty(extended.DefaultTypeName))
            {
                return extended.DefaultTypeName;
            }

            // Use first type from Types array if available
            if (extended.Types.Count > 0)
            {
                return extended.Types[0].Code;
            }
        }

        // Fall back to Info.Name (works for top-level types, not child elements)
        return element.Info.Name;
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
