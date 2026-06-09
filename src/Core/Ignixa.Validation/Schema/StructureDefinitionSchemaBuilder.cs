// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirPath;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Parser;
using Ignixa.Specification;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;
using Microsoft.Extensions.Logging;

namespace Ignixa.Validation.Schema;

/// <summary>
/// Builds ValidationSchema objects from ISchema metadata.
/// Automates the creation of validation checks (RequiredField, Cardinality, Type, Reference)
/// from FHIR StructureDefinition metadata.
/// </summary>
public class StructureDefinitionSchemaBuilder
{
    private readonly FhirPathParser _parser;
    private readonly ILogger<StructureDefinitionSchemaBuilder>? _logger;

    /// <summary>
    /// Per-call cycle guard for the recursive nested-type extraction. Tracks type names
    /// currently being built so that a self-reference (Element->Element, or
    /// BackboneElement->BackboneElement via contentReference) does not recurse forever.
    /// AsyncLocal so concurrent BuildSchema invocations on different threads each get
    /// their own visited set without locking.
    /// </summary>
    private static readonly System.Threading.AsyncLocal<HashSet<string>?> _activeTypeNames = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="StructureDefinitionSchemaBuilder"/> class.
    /// </summary>
    /// <param name="compiler">Shared FhirPath compiler for parsing constraint expressions. If null, a new instance will be created.</param>
    /// <param name="logger">Optional logger for diagnostics during schema building.</param>
    public StructureDefinitionSchemaBuilder(
        FhirPathParser? compiler = null,
        ILogger<StructureDefinitionSchemaBuilder>? logger = null)
    {
        _parser = compiler ?? new FhirPathParser();
        _logger = logger;
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
                // Choice elements are named "value[x]" in the schema, but instances carry
                // concrete names (valueQuantity, valueString, ...). CardinalityCheck counts
                // via IElement.Children, which only performs polymorphic [x] expansion when
                // the requested name has no [x] suffix. Strip it so the check matches the
                // concrete children instead of a literal "value[x]" that never exists.
                var elementName = e.Info.IsChoiceElement && e.Info.Name.EndsWith("[x]", StringComparison.Ordinal)
                    ? e.Info.Name[..^3]
                    : e.Info.Name;

                // Try to get explicit cardinality from extended metadata
                if (e is ITypeExtended extended)
                {
                    int min = extended.Min;
                    int? max = extended.Max == "*" ? null
                        : int.TryParse(extended.Max, out var parsedMax) ? parsedMax
                        : (int?)null;
                    return new CardinalityCheck(elementName, min, max);
                }

                // Fallback to inferred cardinality
                return new CardinalityCheck(
                    elementName,
                    min: e.IsRequired ? 1 : 0,
                    max: e.IsCollection ? (int?)null : 1);
            });
        universalChecks.AddRange(cardinalityChecks);

        // Extract type checks (only for primitive types, moved to Fast tier)
        // This covers ID format validation and other primitive type checks
        // Use element name as the first parameter, and the actual FHIR type from ITypeExtended
        // IMPORTANT: Skip choice elements - they may have a primitive DefaultTypeName (e.g., dateTime)
        // but the actual concrete type depends on the runtime data (e.g., effectivePeriod is a Period object)
        var typeChecks = elements
            .Where(e => e.Info.IsPrimitive && !e.Info.IsChoiceElement)
            .Select(e => new TypeCheck(e.Info.Name, GetTypeName(e)));
        universalChecks.AddRange(typeChecks);

        // Tier 2 (Spec): Schema-driven checks from StructureDefinition
        var specChecks = new List<IValidationCheck>();

        // Extract reference format checks - check the type name, not element name
        // Skip choice elements: their DefaultTypeName may be Reference but the actual type
        // depends on runtime data (e.g., medication[x] could be medicationCodeableConcept)
        var referenceChecks = elements
            .Where(e => !e.Info.IsChoiceElement && GetTypeName(e) == "Reference")
            .Select(e => new ReferenceFormatCheck(e.Info.Name));
        specChecks.AddRange(referenceChecks);

        // Extract coding structure checks - check the type name, not element name
        // Skip choice elements for the same reason as reference checks
        var codingChecks = elements
            .Where(e => !e.Info.IsChoiceElement && GetTypeName(e) is "CodeableConcept" or "Coding")
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
        // Push the current type onto the cycle guard so any recursive Build via
        // ExtractNestedTypeChecks short-circuits if it tries to re-enter this type.
        var visiting = _activeTypeNames.Value ?? new HashSet<string>(StringComparer.Ordinal);
        var ownsVisiting = _activeTypeNames.Value == null;
        if (ownsVisiting)
        {
            _activeTypeNames.Value = visiting;
        }
        var addedToVisiting = visiting.Add(typeDefinition.Info.Name);
        try
        {
            var nestedTypeChecks = ExtractNestedTypeChecks(elements, typeDefinition, schema, terminologyService, _logger);
            specChecks.AddRange(nestedTypeChecks);
        }
        finally
        {
            if (addedToVisiting)
            {
                visiting.Remove(typeDefinition.Info.Name);
            }
            if (ownsVisiting)
            {
                _activeTypeNames.Value = null;
            }
        }

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
    /// <summary>
    /// Well-known FHIR constraint keys to their AppliesTo scope. Used as a fallback when
    /// the constraint source (e.g. codegen <see cref="Ignixa.Abstractions.ConstraintDefinition"/>)
    /// doesn't carry scope metadata. Without this, ext-1 fires on every element that has
    /// an Extension child, dom-* fires on every nested resource, etc.
    /// Keys follow FHIR R4 invariant naming: ele-*, dom-*, ext-*, vs-*, etc.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> WellKnownConstraintScopes =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["ext-1"] = new[] { "Extension" },
        };

    private static IReadOnlyList<string>? ResolveAppliesTo(IConstraint constraint)
    {
        // Source carries scope explicitly (the Specification.ConstraintDefinition path).
        // Cast through object because IConstraint and Specification.ConstraintDefinition
        // are not in the same hierarchy.
        if ((object)constraint is Specification.ConstraintDefinition specConstraint)
        {
            return specConstraint.AppliesTo;
        }

        return WellKnownConstraintScopes.TryGetValue(constraint.Key, out var scope) ? scope : null;
    }

    private static IEnumerable<IValidationCheck> ExtractInvariantChecks(
        IReadOnlyList<IType> elements,
        IType typeDefinition,
        ISchema schema,
        FhirPathParser parser)
    {
        var checks = new List<IValidationCheck>();

        // Deduplicate constraints by key to avoid duplicate checks
        // Multiple elements may reference the same constraint (e.g., ele-1 on every element)
        var seenConstraints = new HashSet<string>(StringComparer.Ordinal);

        // Walk the root type AND each child element. Codegen typically duplicates root-level
        // invariants (ele-1, dom-*) onto every child, but adapter-produced types may keep them
        // only on the root - so we must inspect both to find them all.
        var elementsToScan = new List<IType>(elements.Count + 1) { typeDefinition };
        elementsToScan.AddRange(elements);

        foreach (var element in elementsToScan)
        {
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
                if (seenConstraints.Contains(constraint.Key))
                {
                    continue;
                }

                var appliesTo = ResolveAppliesTo(constraint);

                // Pre-filter: when AppliesTo is explicitly set and excludes this type, skip
                // entirely without consuming the dedup slot, so a later element can still
                // surface the same constraint key if its scope matches.
                if (appliesTo is { Count: > 0 } && !appliesTo.Contains(typeDefinition.Info.Name))
                {
                    continue;
                }

                seenConstraints.Add(constraint.Key);
                checks.Add(new FhirPathInvariantCheck(constraint, schema, parser, appliesTo));
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
        ITerminologyService? terminologyService,
        ILogger? logger = null)
    {
        var checks = new List<IValidationCheck>();

        foreach (var element in elements)
        {
            if (element.Info.IsPrimitive)
            {
                continue;
            }

            if (element.Info.IsChoiceElement)
            {
                continue;
            }

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
            else if (typeName == "Element")
            {
                // Element type might be a BackboneElement in complex datatypes (e.g., Timing.repeat)
                // Try to find a specific type like "Timing.Repeat" first
                var potentialBackboneType = $"{typeDefinition.Info.Name}.{CapitalizeFirst(element.Info.Name)}";
                if (schema.GetTypeDefinition(potentialBackboneType) == null)
                {
                    // No specific BackboneElement type found, skip Element type
                    continue;
                }
                nestedTypeName = potentialBackboneType;
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
                logger?.LogDebug("Nested type '{NestedTypeName}' not found in schema - subtree will not be validated", nestedTypeName);
                continue;
            }

            // Cycle guard: if this nested type is already on the active-build stack
            // (e.g. Element->Element via contentReference, or a profile that recurses
            // through a layered schema provider), skip to avoid infinite recursion.
            var visiting = _activeTypeNames.Value;
            if (visiting != null && visiting.Contains(nestedTypeName))
            {
                logger?.LogDebug("Cycle detected building type '{NestedTypeName}' - skipping to prevent infinite recursion", nestedTypeName);
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
