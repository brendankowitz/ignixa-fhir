/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Centralized type matching logic for FhirPath type operations.
 * Used by: the is and as operators, and the is(), as() and ofType() functions.
 */

using System.Collections.Frozen;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Expressions;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// The single type-matching implementation behind <c>is</c>, <c>is()</c>, <c>as</c>, <c>as()</c> and
/// <c>ofType()</c>.
/// </summary>
/// <remarks>
/// <para>
/// FHIRPath gives all five the identical matching clause - "of the type specified in the second
/// operand, <em>or a subclass thereof</em>" - in both N1/2.0.0 (6.3.1, 6.3.3, 5.2.4) and the 3.0.0
/// build. Nothing in the LANGUAGE spec distinguishes them, so any divergence has to be justified from
/// FHIR's own overrides or it is a bug. They all route through <see cref="IsTypeMatch"/> for that
/// reason: three private copies of this logic had drifted apart, and the drift was invisible because
/// each copy looked reasonable on its own.
/// </para>
/// <para>
/// <strong>What the operators share</strong> is therefore the subclass walk itself: complex subtyping
/// (<c>Age</c>, <c>SimpleQuantity</c> -&gt; <c>Quantity</c>) and the resource hierarchy
/// (<c>Patient</c> -&gt; <c>DomainResource</c> -&gt; <c>Resource</c>). Both are subclass-aware in every
/// operator, which is what makes <c>value as Quantity</c> agree with <c>value is Quantity</c> on an
/// <c>Age</c>. Those two used to disagree, and that disagreement had no defence in any spec.
/// </para>
/// <para>
/// <strong>They differ on exactly two axes</strong>, both of which FHIR - not FHIRPath - states
/// explicitly, and both of which HL7's conformance suite pins. See <see cref="TypeMatchMode"/>.
/// </para>
/// <para>
/// <em>Axis 1, primitive subtyping.</em> R5 2.1.9.1.5 overrides <c>ofType()</c> with "All primitives are
/// considered to be independent types (so <c>markdown</c> is not a subclass of <c>string</c>)", and R6
/// files the same note under a section titled "Function Overrides". The note names only <c>ofType()</c>,
/// but the suite applies it to <c>as()</c> too and pointedly does not apply it to <c>is()</c>: on the
/// same <c>Patient.gender</c> (a <c>code</c>), <c>is(string)</c> must be <c>true</c>
/// (testFHIRPathIsFunction2) while <c>as(string)</c> and <c>ofType(string)</c> must be empty
/// (testFHIRPathAsFunction11/16). It is also load-bearing for indexing: <c>ConceptMap.source</c> is
/// <c>uri|canonical</c> and <c>ConceptMap-source-uri</c> ships as <c>(ConceptMap.source as uri)</c>, so a
/// primitive-inheriting cast would index every <c>sourceCanonical</c> into the <c>source-uri</c>
/// parameter.
/// </para>
/// <para>
/// <em>Axis 2, the System/FHIR namespace.</em> R5 2.1.9.1.2 makes <c>is()</c> the explicit exception -
/// "<c>Patient.name.given.is(System.string).not()</c>" - and then says of the cast: "Note that
/// <c>ofType()</c> does not have such restrictions", declaring both <c>ofType(FHIR.string)</c> and
/// <c>ofType(System.string)</c> valid. HL7's artifacts rely on the leniency: STU3 spells its casts with
/// capitalized System-style names (<c>Patient.deceased.as(DateTime)</c>,
/// <c>Observation.value.as(String)</c>), and R4/R4B's <c>code-value-date</c> composite still carries
/// <c>value.as(DateTime)</c>. Enforcing the namespace distinction on casts would empty those search
/// parameters.
/// </para>
/// <para>
/// <strong>The tension this leaves.</strong> Subtyping is otherwise read off the StructureDefinition
/// graph (<c>baseDefinition</c> where <c>derivation = specialization</c>), and that graph flatly
/// contradicts axis 1: <c>markdown</c> genuinely IS a specialization of <c>string</c>, and R5 declares by
/// fiat that the cast operators must not see it. So "subclass" here cannot be computed from
/// <c>baseDefinition</c> alone for primitives - the FHIR override has to be layered on top, and it is
/// applied narrowly, to the primitive edges of the cast operators only, because that is the only scope
/// the note claims.
/// </para>
/// </remarks>
internal static class TypeMatcher
{
    // System-only types that must match FHIRPath literals (capitalized)
    // These are FHIRPath System types, not FHIR element types
    // Note: Date and Quantity exist as both System types and FHIR types, so they're NOT in this list.
    private static readonly FrozenSet<string> SystemOnlyTypes = new[]
    {
        "Boolean", "Integer", "Decimal", "String", "DateTime", "Time"
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Specialization edges between FHIR primitive types, taken from the StructureDefinitions'
    /// <c>baseDefinition</c> where <c>derivation = specialization</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Followed only under <see cref="TypeMatchMode.TypeTest"/>, i.e. by <c>is</c> and <c>is()</c>.
    /// </para>
    /// <para>
    /// Note what is absent: <c>uri</c> has no entry. Its <c>baseDefinition</c> is <c>Element</c> in R4
    /// and <c>PrimitiveType</c> in R5 - never <c>string</c> - so <c>Questionnaire.url is string</c> is
    /// false. An earlier <c>uri -&gt; string</c> edge here contradicted the very hierarchy this table
    /// claims to encode, and made every URI-flavoured primitive a string by transitivity.
    /// </para>
    /// </remarks>
    private static readonly FrozenDictionary<string, string> PrimitiveTypeInheritance = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["code"] = "string",
        ["id"] = "string",
        ["markdown"] = "string",

        ["url"] = "uri",
        ["canonical"] = "uri",
        ["uuid"] = "uri",
        ["oid"] = "uri",

        ["positiveInt"] = "integer",
        ["unsignedInt"] = "integer"
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Specialization edges between FHIR complex types. Followed by every type operator, under both
    /// <see cref="TypeMatchMode"/> values.
    /// </summary>
    /// <remarks>
    /// <c>SimpleQuantity</c> is a <c>constraint</c> rather than a <c>specialization</c> of
    /// <c>Quantity</c>, but it is listed because FHIR requires it to be selectable that way: "Profiled
    /// types are not allowed, so to select <c>SimpleQuantity</c> one would pass <c>Quantity</c> as an
    /// argument" (R5 2.1.9.1.5). That sentence only means anything if <c>ofType(Quantity)</c> matches a
    /// <c>SimpleQuantity</c> instance.
    /// </remarks>
    private static readonly FrozenDictionary<string, string> ComplexTypeInheritance = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
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
    /// Applied to all five type operators. For <c>is</c>, <c>is()</c>, <c>as</c> and <c>as()</c> this is
    /// spec compliance - the sentence quoted above is stated for both keywords, and the function forms
    /// inherit it by being defined "just as with" their keyword. For <c>ofType()</c> it is a consistency
    /// choice: the spec requires its argument to "resolve to the name of a type in a model" but never
    /// states the failure mode, and the reference engines disagree (HAPI errors, Firely returns empty).
    /// Matching <c>as()</c> keeps one answer to one question inside this engine, and is not claimed as
    /// conformance.
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
    /// - a 0..* path on the left of <c>as</c> - is one of 135 operator-form <c>as</c> occurrences across
    /// 63 shipped R4 SearchParameters, and 136 across 63 in R4B, and the same shape covers
    /// <c>useContext</c> on the canonical resources, Composition's <c>related-id</c>/<c>related-ref</c>,
    /// the Medication and Substance <c>ingredient</c> parameters, Group's <c>value</c> and Goal's
    /// <c>target-date</c>. STU3 is not affected by the operator at all - it spells all of its casts with
    /// the <c>as()</c> function, 57 occurrences across 42 SearchParameters. The composite components add
    /// 57 more <c>as()</c> occurrences in R4 and R4B apiece. (Counts are over
    /// <c>{Version}SearchParameterDefinitions.g.cs</c>, resolving <c>Constants.Expr_*</c> references and
    /// reading both the top-level <c>expression:</c> and the <c>SearchParameterComponentInfo</c>
    /// expressions; recount there rather than trusting these if it matters.) In R5 HL7 rewrote almost
    /// every one to <c>ofType()</c>: the operator survives only in
    /// <c>Bundle.entry[0].resource as X</c> (indexed, so a singleton),
    /// <c>NutritionIntake.reported as Reference</c> (0..1), and
    /// <c>AdverseEvent.suspectEntity.instance as Reference</c>, which is genuinely repeating - see the
    /// note below. Enforcing the rule below R5 would make <c>ElementSearchIndexer</c> throw on any
    /// resource populating one of those repeating paths, and its non-composite path logs and continues -
    /// so the values would vanish from the search index with nothing surfaced to the caller.
    /// </para>
    /// <para>
    /// The one R5 casualty is <c>AdverseEvent</c>'s <c>substance</c> parameter on a resource with more
    /// than one <c>suspectEntity</c>. It costs no index data: <c>instance</c> is a
    /// <c>CodeableReference</c> in R5 and resolves as <c>CodeableConcept</c> here, so
    /// <c>as Reference</c> already matched nothing and the parameter yielded zero entries for one
    /// suspectEntity as much as for two. Enforcement turns a silent zero into a logged zero. The real
    /// defect there is the unresolved <c>CodeableReference</c>, which is out of scope for this rule.
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
    /// Enforces FHIRPath's cardinality rule for the type-TEST operators: "If the input collections
    /// contains more than one item, the evaluator will throw an error" (Types and Reflection, <c>is</c>;
    /// identical in N1 6.3.1 and the 3.0.0 build).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike <see cref="EnsureSingletonInput"/> this takes no schema and is <strong>not</strong> version
    /// gated, and the asymmetry is evidence-based rather than stylistic. The <c>as</c> gate exists solely
    /// because HL7's own R4/R4B SearchParameter definitions put 0..* paths on the left of <c>as</c>, so
    /// enforcing there would silently empty the search index. No shipped artifact does the same to
    /// <c>is</c>: across STU3, R4, R4B, R5 and R6 every <c>is</c> in a SearchParameter expression is the
    /// shape <c>where(resolve() is Patient)</c>, whose focus is a single item by construction, and the
    /// only other occurrence anywhere is STU3's <c>Condition.abatement.is(dateTime)</c> over a 0..1
    /// choice. The invariants agree - they use <c>$this is X</c>, <c>%resource is X</c>, or <c>is</c>
    /// inside <c>where()</c>/<c>exists()</c>/<c>all()</c>, all singletons. With no artifact to break,
    /// there is nothing for a gate to protect and the rule is simply enforced.
    /// </para>
    /// <para>
    /// This is also what <c>is()</c> the function has always done, and the two spellings returning
    /// different answers for the same input was the defect this method removes.
    /// </para>
    /// </remarks>
    public static void EnsureSingletonTypeTestInput(int inputCount, string operatorDescription)
    {
        if (inputCount <= 1)
        {
            return;
        }

        throw new FhirPathEvaluationException(
            $"The input to {operatorDescription} must be a single item, but was a collection of {inputCount} items.");
    }

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
    /// Checks if the element's type matches the target type, walking the FHIR specialization graph.
    /// </summary>
    /// <remarks>
    /// Covers complex subtypes (Age/SimpleQuantity -&gt; Quantity), the resource hierarchy
    /// (Patient -&gt; DomainResource -&gt; Resource) and - when <paramref name="mode"/> is
    /// <see cref="TypeMatchMode.TypeTest"/> - primitive subtypes (code -&gt; string,
    /// positiveInt -&gt; integer, url -&gt; uri).
    ///
    /// Resource hierarchy is determined using type metadata from the schema provider.
    /// Note: Resource and Element are separate branches under Base in the FHIR type system.
    /// This method does not handle Element/DataType hierarchy as it is not needed for
    /// FHIRPath type operations (the official test suite does not test for is(Element)).
    /// </remarks>
    public static bool MatchesTypeWithInheritance(IElement element, string typeName, TypeMatchMode mode)
    {
        var currentType = element.InstanceType;
        if (string.IsNullOrEmpty(currentType))
            return false;

        while (!string.IsNullOrEmpty(currentType))
        {
            if (currentType.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!TryGetBaseType(currentType, mode, out var baseType))
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

    private static bool TryGetBaseType(string typeName, TypeMatchMode mode, out string? baseType)
    {
        if (ComplexTypeInheritance.TryGetValue(typeName, out baseType))
            return true;

        if (mode == TypeMatchMode.TypeTest)
            return PrimitiveTypeInheritance.TryGetValue(typeName, out baseType);

        baseType = null;
        return false;
    }

    /// <summary>
    /// The type test behind every FHIRPath type operator.
    /// </summary>
    /// <remarks>
    /// <paramref name="mode"/> is the only knob any caller turns, and it selects between the two
    /// documented axes of divergence; see <see cref="TypeMatcher"/> for why they exist and why nothing
    /// else about the five operators' matching may differ.
    /// </remarks>
    public static bool IsTypeMatch(IElement element, string typeName, TypeMatchMode mode)
    {
        var (baseTypeName, isSystemNamespace, isFhirNamespace) = ParseTypeName(typeName);

        if (mode == TypeMatchMode.TypeTest && !NamespaceMatches(element, baseTypeName, isSystemNamespace, isFhirNamespace))
            return false;

        return MatchesTypeWithInheritance(element, baseTypeName, mode);
    }

    /// <summary>
    /// Applies FHIR's System-versus-FHIR namespace distinction, which R5 2.1.9.1.2 scopes to the type
    /// TEST operators and explicitly withholds from the casts.
    /// </summary>
    private static bool NamespaceMatches(IElement element, string baseTypeName, bool isSystemNamespace, bool isFhirNamespace)
    {
        // A FHIRPath literal is a System value; anything sourced from the resource tree is a FHIR value.
        var implType = element.GetType().Name;
        bool elementIsSystemType = implType.Contains("Primitive", StringComparison.OrdinalIgnoreCase);

        if (isSystemNamespace)
            return elementIsSystemType;

        if (isFhirNamespace)
            return !elementIsSystemType;

        // Unqualified and capitalized, e.g. String: the FHIRPath System type, so Patient.active is not a
        // Boolean even though it is a boolean.
        return !SystemOnlyTypes.Contains(baseTypeName) || elementIsSystemType;
    }

    /// <summary>
    /// Filters a collection to the elements matching the specified type, for <c>as</c>, <c>as()</c> and
    /// <c>ofType()</c>.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="TypeMatchMode.Cast"/>: subclass-aware over complex types and resources, exact over
    /// primitives, and indifferent to the namespace qualifier. That combination is what lets
    /// <c>ofType(Quantity)</c> select a <c>SimpleQuantity</c> while <c>ofType(string)</c> still rejects a
    /// <c>code</c> and STU3's <c>as(DateTime)</c> still selects a <c>dateTime</c>.
    /// </remarks>
    public static IEnumerable<IElement> FilterByType(IEnumerable<IElement> elements, string typeName) =>
        elements.Where(e => IsTypeMatch(e, typeName, TypeMatchMode.Cast));
}
