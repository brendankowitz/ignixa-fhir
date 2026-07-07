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

        // Bundle-specific structural checks: closed-world rules with no terminology dependency.
        if (typeDefinition.Info.Name == "Bundle")
        {
            universalChecks.Add(new BundleFullUrlCheck());
            universalChecks.Add(new BundleLinkRelationCheck());
        }

        // Attachment.size, when stated, must match the decoded Attachment.data length.
        if (typeDefinition.Info.Name == "Attachment")
        {
            universalChecks.Add(new AttachmentSizeCheck());
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

        // Extract structural-shape checks: enforce that the raw JSON shape of each declared
        // element matches its definition (array-vs-scalar, null, ele-1 emptiness, primitive-vs-object).
        // Choice elements are excluded: their value[x] shape and primitive value rules are handled
        // by ChoiceElementCheck. xhtml is excluded for the same reason as the cardinality pass.
        // "contained" is excluded: it has dedicated handling (ContainedResourceCheck) and an empty
        // contained array is tolerated by established behavior.
        var shapeChecks = elements
            .Where(e => !e.Info.IsChoiceElement
                && GetTypeName(e) != "xhtml"
                && e.Info.Name != "contained")
            .Select(e =>
            {
                var typeName = GetTypeName(e);
                var isBackbone = typeName is "BackboneElement" or "Element";
                return new StructuralShapeCheck(
                    e.Info.Name,
                    e.Info.IsPrimitive,
                    IsCollectionElement(e),
                    isBackbone,
                    typeName);
            });
        specChecks.AddRange(shapeChecks);

        // Mode-gated semantic checks (default OFF; each no-ops unless its ValidationSettings flag is
        // set). Wired per primitive element by declared type so they carry no cost when disabled:
        // string -> embedded-HTML (security-checks); markdown -> embedded-HTML (noHtmlInMarkdown);
        // url/uri/canonical -> example-domain URLs (examples/non-spec mode).
        var primitiveElements = elements.Where(e => e.Info.IsPrimitive && !e.Info.IsChoiceElement).ToList();
        specChecks.AddRange(primitiveElements
            .Where(e => GetTypeName(e) == "string")
            .Select(e => new EmbeddedHtmlStringCheck(e.Info.Name)));
        specChecks.AddRange(primitiveElements
            .Where(e => GetTypeName(e) == "markdown")
            .Select(e => new MarkdownHtmlCheck(e.Info.Name)));
        specChecks.AddRange(primitiveElements
            .Where(e => GetTypeName(e) is "url" or "uri" or "canonical")
            .Select(e => new ExampleUrlCheck(e.Info.Name)));

        // Empty-array rejection (ele-1) at resource altitude: closes the gap StructuralShapeCheck
        // leaves for complex datatypes (CodeableConcept, Coding, ...) that never get their own nested
        // schema, so an empty array nested inside one (e.g. category[0].coding: []) would otherwise
        // go unchecked. Runs once per resource via a single raw-JSON walk; see EmptyArrayCheck remarks.
        if (typeDefinition.Info.IsResource)
        {
            specChecks.Add(new EmptyArrayCheck());
        }

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

        // Children that resolve to their own nested schema (backbone/complex datatypes). A
        // constraint owned exclusively by such an element (e.g. Patient.contact's pat-1) is
        // element-scoped: it is evaluated at that element's altitude by the nested schema, not
        // hoisted to the resource root. Computed under the cycle guard so it matches exactly
        // which elements ExtractNestedTypeChecks descends into.
        var nestedElementNames = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var e in elements)
            {
                if (ResolveNestedType(e, typeDefinition, schema, out _, out _) == NestedTypeResolution.Resolved)
                {
                    nestedElementNames.Add(e.Info.Name);
                }
            }

            var rootScopedConstraintKeys = CollectRootScopedConstraintKeys(elements, typeDefinition, nestedElementNames);
            var nestedTypeChecks = ExtractNestedTypeChecks(elements, typeDefinition, schema, terminologyService, rootScopedConstraintKeys, _logger, _parser);
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
        var invariantChecks = ExtractInvariantChecks(elements, typeDefinition, schema, _parser, nestedElementNames, _logger);
        profileChecks.AddRange(invariantChecks);

        // Reference-integrity (Full tier): flag local references (#id, intra-Bundle Type/id) that
        // fail to resolve against the scoped resolver. No-ops when no resolver is seeded, so it is
        // inert outside the scoped validation pipeline.
        if (typeDefinition.Info.IsResource)
        {
            profileChecks.Add(new ReferenceResolutionCheck());

            // Closed-world, terminology-independent structural rules the HL7 reference validator
            // enforces. Registered in the profile (Full) tier so Compatibility depth is unaffected.
            profileChecks.Add(new ExtensionUrlVersionCheck());
            profileChecks.Add(new ExtensionDefinitionCheck());

            switch (typeDefinition.Info.Name)
            {
                case "ValueSet":
                    profileChecks.Add(new ValueSetIncludeSystemCheck());
                    profileChecks.Add(new ValueSetFilterCheck());
                    break;
                case "CodeSystem":
                    profileChecks.Add(new CodeSystemSupplementContentCheck());
                    profileChecks.Add(new CodeSystemPropertyTypeCheck());
                    break;
            }
        }

        // Choice-variant nested validation (Full tier): a complex value[x] variant (e.g.
        // valueAttachment) is skipped by ChoiceElementCheck, which only recurses into primitive
        // variants. Route each complex variant through its datatype's own schema — reusing
        // NestedComplexTypeCheck against the concrete variant name — so nested rules (base64Binary on
        // Attachment.data, cardinality, shape) apply. A no-op when that variant is absent. Profile
        // tier keeps Compatibility depth unchanged.
        //
        // Seed the cycle guard with the current type first: the `finally` above cleared
        // _activeTypeNames, and BuildChoiceVariantChecks re-enters BuildSchema per complex variant.
        // Without the current type in the visited set, a cyclic choice-variant datatype graph would
        // recurse until it overflowed. Re-establishing it here makes the visited-set guard inside
        // BuildChoiceVariantChecks effective across that recursion.
        var choiceVisiting = _activeTypeNames.Value ?? new HashSet<string>(StringComparer.Ordinal);
        var ownsChoiceVisiting = _activeTypeNames.Value is null;
        if (ownsChoiceVisiting)
        {
            _activeTypeNames.Value = choiceVisiting;
        }
        var addedChoiceType = choiceVisiting.Add(typeDefinition.Info.Name);
        try
        {
            foreach (var choiceElement in elements.Where(e => e.Info.IsChoiceElement))
            {
                profileChecks.AddRange(BuildChoiceVariantChecks(
                    choiceElement, schema, terminologyService, _logger, _parser));
            }
        }
        finally
        {
            if (addedChoiceType)
            {
                choiceVisiting.Remove(typeDefinition.Info.Name);
            }
            if (ownsChoiceVisiting)
            {
                _activeTypeNames.Value = null;
            }
        }

        // Slicing (Full tier): enforce per-slice cardinality and closed/openAtEnd rules for elements
        // whose profile declares named slices. Only elements carrying named slices produce a check —
        // a bare open slicing header (e.g. base Extension.extension, open by url with no named slices)
        // enforces nothing, so no check is created and valid base resources are never rejected.
        var slicingChecks = elements
            .Where(e => e is ITypeExtended ext && ext.Slicing is { Slices.Count: > 0 })
            .Select(e => new SlicingCheck(SlicedElementName(e), ((ITypeExtended)e).Slicing!));
        profileChecks.AddRange(slicingChecks);

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

    /// <summary>
    /// Known FHIR R4.0.1 constraint errata: the published core StructureDefinitions carry an
    /// incorrect FHIRPath expression that the HL7 reference validator evaluates with a correction.
    /// Keyed by constraint key, each entry replaces one exact source expression with its corrected
    /// form. The match is on the full expression so we only ever touch the precise erratum; if the
    /// generated schema is regenerated with a fixed expression, the substitution silently no-ops.
    /// <para>
    /// que-12 shipped as <c>enableWhen.count() &gt; 2</c> in R4.0.1 (should be <c>&gt;= 2</c> per the
    /// human text "If there are more than one enableWhen"); corrected in R4B/R5. Without the fix an
    /// item with exactly two enableWhen and no enableBehavior is wrongly accepted.
    /// </para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (string From, string To)> ConstraintExpressionErrata =
        new Dictionary<string, (string From, string To)>(StringComparer.Ordinal)
        {
            ["que-12"] = (
                "enableWhen.count() > 2 implies enableBehavior.exists()",
                "enableWhen.count() >= 2 implies enableBehavior.exists()"),
        };

    /// <summary>
    /// Datatype invariants absent from the generated core schema (the codegen emits
    /// <c>constraints: null</c> for complex datatypes such as Period) but enforced by the HL7
    /// reference validator. Injected into the datatype's own build so they evaluate once per
    /// occurrence at that element's altitude, exactly like any element-owned constraint. Keyed by
    /// the datatype name.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<IConstraint>> SupplementalDatatypeConstraints =
        new Dictionary<string, IReadOnlyList<IConstraint>>(StringComparer.Ordinal)
        {
            ["Period"] = new IConstraint[]
            {
                new Ignixa.Abstractions.ConstraintDefinition
                {
                    Key = "per-1",
                    Severity = "error",
                    Human = "If present, start SHALL have a lower or equal value than end",
                    Expression = "start.hasValue().not() or end.hasValue().not() or (start <= end)",
                    Xpath = null,
                },
            },
        };

    /// <summary>
    /// Applies a known-erratum correction to a constraint's FHIRPath expression, if one is registered
    /// for its key and the source expression matches exactly. Returns the original constraint
    /// otherwise.
    /// </summary>
    private static IConstraint NormalizeConstraint(IConstraint constraint)
    {
        if (ConstraintExpressionErrata.TryGetValue(constraint.Key, out var erratum)
            && string.Equals(constraint.Expression, erratum.From, StringComparison.Ordinal))
        {
            return new Ignixa.Abstractions.ConstraintDefinition
            {
                Key = constraint.Key,
                Severity = constraint.Severity,
                Human = constraint.Human,
                Expression = erratum.To,
                Xpath = constraint.Xpath,
            };
        }

        return constraint;
    }

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
        FhirPathParser parser,
        IReadOnlySet<string> nestedElementNames,
        ILogger? logger = null)
    {
        var checks = new List<IValidationCheck>();

        // Deduplicate constraints by key to avoid duplicate checks
        // Multiple elements may reference the same constraint (e.g., ele-1 on every element)
        var seenConstraints = new HashSet<string>(StringComparer.Ordinal);

        // Walk the root type AND each NON-NESTED child element. Codegen typically duplicates
        // root-level invariants (ele-1, dom-*) onto every child, but adapter-produced types may
        // keep them only on the root - so we must inspect both to find them all. Nested
        // complex/backbone children are excluded: a constraint owned only by such an element
        // (e.g. Patient.contact's pat-1) is element-scoped and is evaluated at that element's
        // altitude by its nested schema (see ExtractNestedTypeChecks), not at the resource root.
        var elementsToScan = new List<IType>(elements.Count + 1) { typeDefinition };
        elementsToScan.AddRange(elements.Where(e => !nestedElementNames.Contains(e.Info.Name)));

        // Count how often each constraint key appears across the scanned elements. Universal
        // invariants (ele-1, ext-1, dom-*) are duplicated by codegen onto many child elements, so
        // hoisting them to the type altitude is safe — their expressions are container-relative. A
        // key that appears on a SINGLE primitive child is element-scoped (e.g. eld-3 on
        // ElementDefinition.max, whose expression `empty() or ($this='*') or (toInteger()>=0)` treats
        // the focus as the max scalar). Hoisting such a constraint to the type altitude evaluates it
        // against the whole complex object, where it can never satisfy and spuriously fails on every
        // occurrence. Only the type root's own constraints, and child constraints that recur across
        // multiple elements, are hoisted; single-child element-scoped constraints are left out here
        // (they would need per-element-altitude evaluation, tracked as future work).
        var childKeyOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var element in elements.Where(e => !nestedElementNames.Contains(e.Info.Name)))
        {
            if (element is ITypeExtended ext && ext.Constraints is { Count: > 0 } cs)
            {
                foreach (var c in cs)
                {
                    childKeyOccurrences[c.Key] = childKeyOccurrences.GetValueOrDefault(c.Key) + 1;
                }
            }
        }

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

            var isRootType = ReferenceEquals(element, typeDefinition);

            foreach (var constraint in constraints)
            {
                if (seenConstraints.Contains(constraint.Key))
                {
                    continue;
                }

                // Element-scoped constraint owned by exactly one primitive child: skip hoisting.
                if (!isRootType && childKeyOccurrences.GetValueOrDefault(constraint.Key) <= 1)
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
                checks.Add(new FhirPathInvariantCheck(NormalizeConstraint(constraint), schema, parser, appliesTo, logger));
            }
        }

        // Inject datatype invariants missing from codegen (e.g. Period's per-1). These belong to the
        // type currently being built, so they run at this altitude once per occurrence.
        if (SupplementalDatatypeConstraints.TryGetValue(typeDefinition.Info.Name, out var supplemental))
        {
            foreach (var constraint in supplemental)
            {
                if (seenConstraints.Add(constraint.Key))
                {
                    checks.Add(new FhirPathInvariantCheck(constraint, schema, parser, appliesTo: null, logger));
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
        ITerminologyService? terminologyService,
        IReadOnlySet<string> rootScopedConstraintKeys,
        ILogger<StructureDefinitionSchemaBuilder>? logger = null,
        FhirPathParser? parser = null)
    {
        var checks = new List<IValidationCheck>();

        foreach (var element in elements)
        {
            switch (ResolveNestedType(element, typeDefinition, schema, out var nestedTypeName, out var nestedTypeDefinition))
            {
                case NestedTypeResolution.NotNested:
                    continue;
                case NestedTypeResolution.NotFound:
                    logger?.LogWarning("Nested type '{NestedTypeName}' not found in schema - subtree will not be validated", nestedTypeName);
                    continue;
                case NestedTypeResolution.Cycle:
                    // Element->Element via contentReference, or a profile that recurses through a
                    // layered schema provider. Skip to avoid infinite recursion.
                    logger?.LogDebug("Cycle detected building type '{NestedTypeName}' - skipping to prevent infinite recursion", nestedTypeName);
                    continue;
            }

            // Build the nested schema
            var nestedBuilder = new StructureDefinitionSchemaBuilder(parser, logger);
            var nestedSchema = nestedBuilder.BuildSchema(nestedTypeDefinition!, schema, terminologyService);

            // Inject constraints owned by THIS element (e.g. Patient.contact's pat-1) into the
            // nested schema so they are evaluated once per occurrence, in the element's own
            // FHIRPath context, and skipped entirely when the element is absent. Universal
            // constraints already hoisted to the resource root (ele-1, ext-1) are excluded to
            // avoid duplicate evaluation. Parser is always supplied by BuildSchema; guard keeps
            // the nullable signature honest.
            if (parser is not null)
            {
                var elementScopedChecks = BuildElementScopedInvariantChecks(
                    element, rootScopedConstraintKeys, schema, parser, logger);
                if (elementScopedChecks.Count > 0)
                {
                    var injection = new ValidationSchema(
                        nestedSchema.CanonicalUrl,
                        nestedSchema.ResourceType,
                        universalChecks: Array.Empty<IValidationCheck>(),
                        specChecks: Array.Empty<IValidationCheck>(),
                        profileChecks: elementScopedChecks);
                    nestedSchema = ValidationSchema.Compose(new[] { nestedSchema, injection });
                }
            }

            // Create the nested type check
            var check = new NestedComplexTypeCheck(element.Info.Name, element.IsCollection, nestedSchema);
            checks.Add(check);
        }

        return checks;
    }

    /// <summary>
    /// Outcome of resolving whether a child element has its own nested validation schema.
    /// </summary>
    private enum NestedTypeResolution
    {
        /// <summary>The element is a primitive, choice, or specially-handled type - no nested schema.</summary>
        NotNested,

        /// <summary>A nested type definition was found and is safe to build.</summary>
        Resolved,

        /// <summary>The declared nested type name could not be resolved in the schema.</summary>
        NotFound,

        /// <summary>The nested type is already on the active-build stack (recursion cycle).</summary>
        Cycle,
    }

    /// <summary>
    /// Determines whether a child element has its own nested validation schema (BackboneElement or
    /// complex datatype), and if so resolves the nested type definition. Shared by
    /// <see cref="ExtractNestedTypeChecks"/> (which builds the schema) and the pre-pass in
    /// <see cref="BuildSchema"/> (which classifies constraints as root- vs element-scoped), so both
    /// agree exactly on which elements form a nested altitude.
    /// </summary>
    private static NestedTypeResolution ResolveNestedType(
        IType element,
        IType typeDefinition,
        ISchema schema,
        out string nestedTypeName,
        out IType? nestedTypeDefinition)
    {
        nestedTypeName = string.Empty;
        nestedTypeDefinition = null;

        if (element.Info.IsPrimitive || element.Info.IsChoiceElement)
        {
            return NestedTypeResolution.NotNested;
        }

        var typeName = GetTypeName(element);

        // No type found, or type is same as element name (no extended metadata) - skip.
        if (string.IsNullOrEmpty(typeName) || typeName == element.Info.Name)
        {
            return NestedTypeResolution.NotNested;
        }

        // Special types have dedicated checks; xhtml stores content directly; "Resource" is a
        // contained resource handled by ContainedResourceCheck.
        if (typeName is "Reference" or "CodeableConcept" or "Coding" or "Extension" or "xhtml" or "Resource")
        {
            return NestedTypeResolution.NotNested;
        }

        if (typeName == "BackboneElement")
        {
            // BackboneElement: ResourceType.ElementName (e.g., "AuditEvent.Agent").
            nestedTypeName = $"{typeDefinition.Info.Name}.{CapitalizeFirst(element.Info.Name)}";
        }
        else if (typeName == "Element")
        {
            // Element type might be a BackboneElement in complex datatypes (e.g., Timing.repeat).
            // Only treat it as nested when a specific type (e.g. "Timing.Repeat") exists.
            var potentialBackboneType = $"{typeDefinition.Info.Name}.{CapitalizeFirst(element.Info.Name)}";
            if (schema.GetTypeDefinition(potentialBackboneType) == null)
            {
                return NestedTypeResolution.NotNested;
            }
            nestedTypeName = potentialBackboneType;
        }
        else
        {
            // Complex datatype: use as-is (e.g., "Address", "HumanName").
            nestedTypeName = typeName;
        }

        var resolved = schema.GetTypeDefinition(nestedTypeName);
        if (resolved == null)
        {
            return NestedTypeResolution.NotFound;
        }

        var visiting = _activeTypeNames.Value;
        if (visiting != null && visiting.Contains(nestedTypeName))
        {
            return NestedTypeResolution.Cycle;
        }

        nestedTypeDefinition = resolved;
        return NestedTypeResolution.Resolved;
    }

    /// <summary>
    /// Collects the constraint keys that are scoped to the resource root: keys carried by the root
    /// type node or by any non-nested child element. Constraints that appear ONLY on nested
    /// complex/backbone children are element-scoped and are excluded here so they can be evaluated
    /// at the element's altitude instead of the resource root.
    /// </summary>
    private static HashSet<string> CollectRootScopedConstraintKeys(
        IReadOnlyList<IType> elements,
        IType typeDefinition,
        IReadOnlySet<string> nestedElementNames)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        AddConstraintKeys(typeDefinition, keys);

        foreach (var element in elements)
        {
            if (nestedElementNames.Contains(element.Info.Name))
            {
                continue;
            }

            AddConstraintKeys(element, keys);
        }

        return keys;
    }

    private static void AddConstraintKeys(IType element, HashSet<string> keys)
    {
        if (element is ITypeExtended extended && extended.Constraints is { Count: > 0 } constraints)
        {
            foreach (var constraint in constraints)
            {
                keys.Add(constraint.Key);
            }
        }
    }

    /// <summary>
    /// Builds invariant checks for the constraints owned by a nested element that are NOT already
    /// evaluated at the resource root (i.e. genuinely element-scoped invariants such as pat-1).
    /// These are attached to the element's nested schema so they run once per occurrence in the
    /// element's own FHIRPath context.
    /// </summary>
    private static List<IValidationCheck> BuildElementScopedInvariantChecks(
        IType element,
        IReadOnlySet<string> rootScopedConstraintKeys,
        ISchema schema,
        FhirPathParser parser,
        ILogger? logger)
    {
        var checks = new List<IValidationCheck>();

        if (element is not ITypeExtended extended || extended.Constraints is not { Count: > 0 } constraints)
        {
            return checks;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var constraint in constraints)
        {
            // Universal constraints (ele-1, ext-1, ...) are already hoisted to the resource root.
            if (rootScopedConstraintKeys.Contains(constraint.Key))
            {
                continue;
            }

            if (!seen.Add(constraint.Key))
            {
                continue;
            }

            var appliesTo = ResolveAppliesTo(constraint);
            checks.Add(new FhirPathInvariantCheck(NormalizeConstraint(constraint), schema, parser, appliesTo, logger));
        }

        return checks;
    }

    /// <summary>
    /// Builds nested-schema checks for the complex variants of a choice element. Primitive variants
    /// are handled by <see cref="ChoiceElementCheck"/>; specially-handled datatypes (Reference,
    /// CodeableConcept, Coding, Extension, Resource, xhtml) are left to their own dedicated checks to
    /// keep the newly-lit validation surface narrow. Each remaining complex variant yields a
    /// <see cref="NestedComplexTypeCheck"/> targeting the concrete variant name (e.g. "valueAttachment").
    /// </summary>
    private static IEnumerable<IValidationCheck> BuildChoiceVariantChecks(
        IType choiceElement,
        ISchema schema,
        ITerminologyService? terminologyService,
        ILogger<StructureDefinitionSchemaBuilder>? logger,
        FhirPathParser? parser)
    {
        if (choiceElement is not ITypeExtended extended || extended.Types.Count == 0)
        {
            yield break;
        }

        var baseName = choiceElement.Info.Name.EndsWith("[x]", StringComparison.Ordinal)
            ? choiceElement.Info.Name[..^3]
            : choiceElement.Info.Name;

        var isCollection = IsCollectionElement(choiceElement);
        var visiting = _activeTypeNames.Value;
        var seenTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var typeReference in extended.Types)
        {
            var typeName = typeReference.Code;
            if (string.IsNullOrEmpty(typeName)
                || !seenTypes.Add(typeName)
                || IsPrimitiveType(typeName)
                || typeName is "Reference" or "CodeableConcept" or "Coding" or "Extension" or "xhtml" or "Resource"
                || (visiting is not null && visiting.Contains(typeName)))
            {
                continue;
            }

            var typeDefinition = schema.GetTypeDefinition(typeName);
            if (typeDefinition is null)
            {
                continue;
            }

            var nestedSchema = new StructureDefinitionSchemaBuilder(parser, logger)
                .BuildSchema(typeDefinition, schema, terminologyService);

            yield return new ChoiceVariantNestedCheck(
                baseName + CapitalizeFirst(typeName), isCollection, nestedSchema);
        }
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
    /// Determines whether an element is a collection (max &gt; 1 / "*").
    /// Prefers the explicit <see cref="ITypeExtended.Max"/> value, matching the cardinality pass,
    /// and falls back to <see cref="IType.IsCollection"/> when extended metadata is unavailable.
    /// </summary>
    /// <param name="element">The element to inspect.</param>
    /// <returns>True if the element accepts more than one occurrence.</returns>
    private static bool IsCollectionElement(IType element)
    {
        if (element is ITypeExtended extended)
        {
            if (extended.Max == "*")
            {
                return true;
            }

            if (int.TryParse(extended.Max, out var parsedMax))
            {
                return parsedMax > 1;
            }
        }

        // Fall back to IsCollection when Max is absent/unparseable, to avoid a false
        // "scalar" verdict from incomplete extended metadata.
        return element.IsCollection;
    }

    /// <summary>
    /// The name used to navigate the sliced array via <see cref="IElement.Children(string)"/>.
    /// Strips a choice <c>[x]</c> suffix so navigation matches the concrete typed children, mirroring
    /// the cardinality pass.
    /// </summary>
    private static string SlicedElementName(IType element)
        => element.Info.Name.EndsWith("[x]", StringComparison.Ordinal)
            ? element.Info.Name[..^3]
            : element.Info.Name;

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
