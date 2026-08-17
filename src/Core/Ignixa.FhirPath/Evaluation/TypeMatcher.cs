/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Centralized type matching logic for FhirPath type operations.
 * Used by: is operator, as operator, ofType() function, as() function.
 */

using System.Collections.Frozen;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Expressions;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// Provides centralized type matching logic for FhirPath type operations.
/// </summary>
internal static class TypeMatcher
{
    // System-only types that must match FHIRPath literals (capitalized)
    // These are FHIRPath System types, not FHIR element types
    // Note: Date and Quantity exist as both System types and FHIR types, so they're NOT in this list.
    private static readonly FrozenSet<string> SystemOnlyTypes = new[]
    {
        "Boolean", "Integer", "Decimal", "String", "DateTime", "Time"
    }.ToFrozenSet(StringComparer.Ordinal);

    // FHIR type inheritance mappings (subtype -> base type)
    private static readonly FrozenDictionary<string, string> TypeInheritance = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // String subtypes
        ["code"] = "string",
        ["id"] = "string",
        ["markdown"] = "string",
        ["uri"] = "string",

        // URI subtypes (uri -> string)
        ["url"] = "uri",
        ["canonical"] = "uri",
        ["uuid"] = "uri",
        ["oid"] = "uri",

        // Integer subtypes
        ["positiveInt"] = "integer",
        ["unsignedInt"] = "integer",

        // Quantity subtypes
        ["Age"] = "Quantity",
        ["Count"] = "Quantity",
        ["Distance"] = "Quantity",
        ["Duration"] = "Quantity",
        ["Money"] = "Quantity",
        ["SimpleQuantity"] = "Quantity"
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> ResourcesNotExtendingDomainResource = new[]
    {
        "Bundle", "Parameters", "Binary"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // The FHIRPath System namespace. These are types of the language itself, so no FHIR model declares
    // them and asking the schema about them would wrongly report them as unresolvable.
    private static readonly FrozenSet<string> SystemTypeNames = new[]
    {
        "Boolean", "String", "Integer", "Long", "Decimal", "Date", "DateTime", "Time", "Quantity"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Enforces FHIRPath's rule for the type operators: "if the identifier cannot be resolved to a valid
    /// type identifier, the evaluator will throw an error" (Types and Reflection, <c>is</c>/<c>as</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deciding this needs a model, and <paramref name="schema"/> is optional on
    /// <see cref="EvaluationContext"/>: with no model there is no table to fail a lookup against, and
    /// treating "we were given no model" as "the identifier is wrong" would reject valid expressions. So
    /// an absent schema keeps the pre-existing permissive behaviour rather than guessing.
    /// </para>
    /// <para>
    /// Applied to <c>as</c>, <c>as()</c> and <c>ofType()</c>. For the first two this is spec compliance.
    /// For <c>ofType()</c> it is a consistency choice: the spec requires its argument to "resolve to the
    /// name of a type in a model" but never states the failure mode, and the reference engines disagree
    /// (HAPI errors, Firely returns empty). Matching <c>as()</c> keeps one answer to one question inside
    /// this engine, and is not claimed as conformance.
    /// </para>
    /// </remarks>
    public static void EnsureTypeIdentifierResolves(string typeName, ISchema? schema, string operatorDescription)
    {
        if (schema is null)
        {
            return;
        }

        var (baseTypeName, _, _) = ParseTypeName(typeName);

        if (SystemTypeNames.Contains(baseTypeName) || schema.IsKnownType(baseTypeName))
        {
            return;
        }

        throw new FhirPathEvaluationException(
            $"'{typeName}' in {operatorDescription} is not a type identifier known to the {schema.Version} model.");
    }

    /// <summary>
    /// Enforces FHIRPath's cardinality rule for the type-cast operators from R5 onwards: "if there is
    /// more than one item in the input collection, the evaluator will throw an error" (Types and
    /// Reflection, <c>as</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Applied to <c>as</c> and to <c>as()</c>, which the spec defines as existing "for backwards
    /// compatibility ... just as with the <c>as</c> keyword", so the keyword's rules carry over. Empty
    /// input stays empty - the rule is about more than one item, not about exactly one.
    /// </para>
    /// <para>
    /// Deliberately NOT applied to <c>ofType()</c>. That function is specified as a filter over a
    /// collection ("returns a collection that contains all items in the input collection that are of the
    /// given type"), so a multi-item input is its normal case, not an error.
    /// </para>
    /// <para>
    /// The version gate is the whole point of this method, and it is not arbitrary leniency: HL7's own
    /// SearchParameter definitions break the rule below R5. <c>Observation.component.value as Quantity</c>
    /// - a 0..* path on the left of <c>as</c> - is one of 58 operator-form <c>as</c> expressions in the
    /// shipped R4 definitions and 59 in R4B, and the same shape covers <c>useContext</c> on the
    /// canonical resources, Composition's <c>related-id</c>/<c>related-ref</c>, the Medication and
    /// Substance <c>ingredient</c> parameters, Group's <c>value</c> and Goal's <c>target-date</c>. STU3
    /// is not affected by the operator at all - it spells all 50 of its casts with the <c>as()</c>
    /// function. In R5 HL7 rewrote almost every one to <c>ofType()</c>: the operator survives only in
    /// <c>Bundle.entry[0].resource as X</c> (indexed, so a singleton),
    /// <c>NutritionIntake.reported as Reference</c> (0..1), and
    /// <c>AdverseEvent.suspectEntity.instance as Reference</c>, which is genuinely repeating - see the
    /// note below. Enforcing the rule below R5 would make <c>ElementSearchIndexer</c> throw on any
    /// resource populating one of those repeating paths - once it supplies a schema, which today it
    /// deliberately does not - and its non-composite path logs and continues, so the values would vanish
    /// from the search index with nothing surfaced to the caller. The version gate is what keeps that
    /// true if the schema is ever supplied.
    /// </para>
    /// <para>
    /// The one R5 candidate is <c>AdverseEvent</c>'s <c>substance</c> parameter -
    /// <c>(AdverseEvent.suspectEntity.instance as Reference)</c> - on a resource with more than one
    /// <c>suspectEntity</c>. It costs no index data, for two independent reasons, and it is worth being
    /// precise about both because each one on its own is something a reasonable person might remove.
    /// </para>
    /// <para>
    /// First, the cast matches nothing to begin with. R5 declares <c>instance</c> as a
    /// <c>CodeableReference</c>; the generated schema flattens that into a choice of
    /// <c>CodeableConcept | Reference</c> whose <c>defaultTypeName</c> is <c>CodeableConcept</c>, so real
    /// R5 wire data - <c>"instance": { "reference": { "reference": "Substance/s1" } }</c> - resolves with
    /// an <c>InstanceType</c> of <c>CodeableConcept</c>. Measured through the indexer: <c>instance</c>
    /// resolves to 2 elements for two suspectEntity, <c>as Reference</c> selects 0 of them, and the
    /// parameter yields zero entries for one suspectEntity as much as for two.
    /// </para>
    /// <para>
    /// Second, and this is the part that is easy to state wrongly: enforcement does not reach the write
    /// path at all. <c>ElementSearchIndexer.Extract</c> builds its <see cref="FhirEvaluationContext"/>
    /// without a <c>Schema</c>, and both this method and
    /// <see cref="EnsureTypeIdentifierResolves"/> no-op when the schema is null - so on the indexing path
    /// the input count of 2 is never tested and nothing is thrown or logged. The zero above is silent
    /// today and stays silent; do not read this rule as converting it into a reported error.
    /// </para>
    /// <para>
    /// The trap is that those two protections are independent, so removing either alone looks safe while
    /// removing both is not. Fixing the <c>CodeableReference</c> resolution so <c>instance</c> resolves as
    /// <c>Reference</c> would, on its own, make the parameter start indexing 2 entries - an improvement,
    /// because the schema is still null there. Setting <c>Schema</c> on the indexer would, on its own,
    /// change nothing observable, because the cast still matches nothing. Doing both turns those 2 entries
    /// into a thrown <see cref="FhirPathEvaluationException"/> that the non-composite path logs and
    /// swallows, and the data that the first fix recovered is lost again. Whoever fixes the
    /// <c>CodeableReference</c> resolution owns this interaction.
    /// </para>
    /// <para>
    /// This is exactly HAPI's rule (<c>doNotEnforceAsSingletonRule</c> is true below R5, for the same
    /// reason), so the two engines now agree on every version. Firely never enforces it and applies
    /// <c>as</c> element-wise, so <c>Patient.name.as(HumanName)</c> returns three names there and throws
    /// here on R5+ only. The official suite's <c>testFHIRPathAsFunction21</c> marks the multi-item case
    /// invalid in all three versions; we follow it from R5, which is where HL7's artifacts stopped
    /// contradicting it.
    /// </para>
    /// <para>
    /// Failing open is deliberate when the version cannot be established. An absent schema carries no
    /// version at all, and <see cref="FhirVersion.Unspecified"/> means the version could not be
    /// determined - even though its numeric value sorts above <see cref="FhirVersion.R5"/> and the
    /// codebase's usual <c>version &gt;= FhirVersion.R5</c> idiom would therefore enforce. That idiom is
    /// deliberately not used here: the two ways to be wrong are not symmetric. Enforcing when we should
    /// not silently drops search index entries; not enforcing when we should returns a collection where
    /// the spec wanted an error, which is what Firely does anyway.
    /// </para>
    /// </remarks>
    public static void EnsureSingletonInput(int inputCount, ISchema? schema, string operatorDescription)
    {
        if (inputCount <= 1 || !EnforcesSingletonCast(schema))
        {
            return;
        }

        throw new FhirPathEvaluationException(
            $"The input to {operatorDescription} must be a single item, but was a collection of {inputCount} items. " +
            $"This rule is enforced from FHIR R5 onwards, where HL7's own artifacts use ofType() for repeating paths.");
    }

    private static bool EnforcesSingletonCast(ISchema? schema) =>
        schema is not null
        && schema.Version != FhirVersion.Unspecified
        && schema.Version >= FhirVersion.R5;

    /// <summary>
    /// Extracts the type name from a FhirPath expression.
    /// Handles: System.Boolean, FHIR.Patient, Boolean, Patient, `Patient`
    /// </summary>
    public static string? ExtractTypeName(Expression expr)
    {
        return expr switch
        {
            IdentifierExpression idExpr => idExpr.Name,
            PropertyAccessExpression propExpr => ExtractPropertyAccessTypeName(propExpr),
            FunctionCallExpression funcExpr => funcExpr.FunctionName,
            ConstantExpression constExpr => constExpr.Value?.ToString(),
            _ => null
        };
    }

    private static string ExtractPropertyAccessTypeName(PropertyAccessExpression propExpr)
    {
        // Use Stack to avoid O(n²) from List.Insert(0, ...)
        var parts = new Stack<string>();
        Expression? current = propExpr;
        
        while (current is PropertyAccessExpression prop)
        {
            parts.Push(prop.PropertyName);
            current = prop.Focus;
        }

        if (current is IdentifierExpression id)
        {
            parts.Push(id.Name);
        }

        return string.Join(".", parts);
    }

    /// <summary>
    /// Parses a type name and removes namespace prefix if present.
    /// Returns the base type name and flags for explicit namespaces.
    /// </summary>
    public static (string TypeName, bool IsSystemNamespace, bool IsFhirNamespace) ParseTypeName(string typeName)
    {
        if (typeName.StartsWith("System.", StringComparison.OrdinalIgnoreCase))
        {
            return (typeName.Substring(7), true, false);
        }
        
        if (typeName.StartsWith("FHIR.", StringComparison.OrdinalIgnoreCase))
        {
            return (typeName.Substring(5), false, true);
        }

        return (typeName, false, false);
    }

    /// <summary>
    /// Removes namespace prefix from a type name for simple matching.
    /// </summary>
    public static string StripNamespace(string typeName)
    {
        // Optimized to avoid string.Split allocation
        var dotIndex = typeName.IndexOf('.', StringComparison.Ordinal);
        if (dotIndex > 0 && typeName.LastIndexOf('.') == dotIndex)
        {
            var prefix = typeName.AsSpan(0, dotIndex);
            if (prefix.Equals("FHIR", StringComparison.OrdinalIgnoreCase) ||
                prefix.Equals("System", StringComparison.OrdinalIgnoreCase))
            {
                return typeName.Substring(dotIndex + 1);
            }
        }
        return typeName;
    }

    /// <summary>
    /// Checks if the element's type matches the target type (simple matching, no inheritance).
    /// </summary>
    public static bool MatchesType(IElement element, string typeName)
    {
        var elementType = element.InstanceType;
        if (string.IsNullOrEmpty(elementType))
            return false;

        return elementType.Equals(typeName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if the element's type matches the target type, considering FHIR type inheritance.
    /// </summary>
    /// <remarks>
    /// Supports:
    /// - Primitive type inheritance (e.g., code->string, uri->string, positiveInt->integer)
    /// - Quantity subtypes (e.g., Age->Quantity, Duration->Quantity)
    /// - FHIR resource hierarchy (e.g., Patient->DomainResource->Resource)
    ///
    /// Resource hierarchy is determined using type metadata from the schema provider.
    /// Note: Resource and Element are separate branches under Base in the FHIR type system.
    /// This method does not handle Element/DataType hierarchy as it is not needed for
    /// FHIRPath type operations (the official test suite does not test for is(Element)).
    /// </remarks>
    public static bool MatchesTypeWithInheritance(IElement element, string typeName)
    {
        var currentType = element.InstanceType;
        if (string.IsNullOrEmpty(currentType))
            return false;

        while (!string.IsNullOrEmpty(currentType))
        {
            if (currentType.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!TypeInheritance.TryGetValue(currentType, out var baseType))
                break;

            currentType = baseType;
        }

        if (element.Type?.Info is { IsResource: true })
        {
            if (typeName.Equals("Resource", StringComparison.OrdinalIgnoreCase))
                return true;

            if (typeName.Equals("DomainResource", StringComparison.OrdinalIgnoreCase))
            {
                var instanceType = element.InstanceType;
                return !ResourcesNotExtendingDomainResource.Contains(instanceType);
            }
        }

        return false;
    }

    /// <summary>
    /// Full type checking for the 'is' operator with System/FHIR namespace handling.
    /// </summary>
    public static bool IsTypeMatch(IElement element, string typeName)
    {
        var (baseTypeName, isSystemNamespace, isFhirNamespace) = ParseTypeName(typeName);
        var elementType = element.InstanceType ?? string.Empty;

        // Check if element is a FHIRPath literal (System type)
        var implType = element.GetType().Name;
        bool elementIsSystemType = implType.Contains("Primitive", StringComparison.OrdinalIgnoreCase);

        // With explicit namespace, enforce strict matching
        if (isSystemNamespace && !elementIsSystemType)
            return false;

        if (isFhirNamespace && elementIsSystemType)
            return false;

        if (!isSystemNamespace && !isFhirNamespace && SystemOnlyTypes.Contains(baseTypeName) && !elementIsSystemType)
            return false;

        // Compare types with inheritance
        return MatchesTypeWithInheritance(element, baseTypeName);
    }

    /// <summary>
    /// Filters a collection to elements matching the specified type.
    /// </summary>
    public static IEnumerable<IElement> FilterByType(IEnumerable<IElement> elements, string typeName, bool useInheritance = false)
    {
        var strippedTypeName = StripNamespace(typeName);
        
        return useInheritance 
            ? elements.Where(e => MatchesTypeWithInheritance(e, strippedTypeName))
            : elements.Where(e => MatchesType(e, strippedTypeName));
    }
}
