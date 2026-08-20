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
/// <em>Axis 2, type-name spelling across the System/FHIR namespace.</em> This rule changes with the FHIR
/// version. R4 and R4B 2.1.9.1.2 say that <c>as()</c> "does not have such restrictions" and explicitly
/// allow both <c>as(FHIR.string)</c> and <c>as(System.string)</c>. R5 changes that sentence to name only
/// <c>ofType()</c>, withdrawing the allowance from <c>as</c>. HAPI follows that boundary exactly:
/// <c>doNotEnforceAsCaseSensitive</c> is true only below R5. Ignixa therefore matches type names exactly
/// in every version, but below R5 lets the canonical System spellings cross to the corresponding FHIR
/// primitive for the cast operators. Arbitrary spellings such as <c>DATETIME</c> and
/// <c>dAtEtImE</c> remain invalid matches; FHIRPath is not a case-insensitive language.
/// </para>
/// <para>
/// HL7's artifacts corroborate the same boundary. The shipped definitions contain 11 capitalized casts
/// in STU3 (<c>DateTime</c> x6, <c>Date</c> x2, <c>String</c> x1, <c>Uri</c> x2), 1 in R4, 1 in R4B and
/// none in R5 or R6. The R4/R4B occurrence is the <c>code-value-date</c> composite's
/// <c>value.as(DateTime) | value.as(Period)</c>; removing the legacy crossing would drop its date
/// component and therefore the whole composite index entry. The STU3 <c>Date</c> pair is
/// <c>Goal-start-date</c> (<c>Goal.start.as(Date)</c>) and <c>Goal-target-date</c>
/// (<c>Goal.target.due.as(Date)</c>). <c>Date</c> is in the alias table for those two artifacts as much
/// as for the System spelling, so an audit that reconciles the table against the System story alone
/// will find it unaccounted for and delete it, silently emptying both parameters.
/// <c>AsOperatorSearchParameterCardinalityTests</c> pins all three artifacts.
/// </para>
/// <para>
/// What is <em>absent</em> from the alias tables carries as much weight. The same scan also finds
/// <c>Quantity</c> x7 in STU3 and x19 in R4/R4B, plus <c>CodeableConcept</c>, <c>Period</c>,
/// <c>Range</c>, <c>Age</c>, <c>Identifier</c> and <c>Reference</c>. None of those is mis-cased: FHIR
/// spells its complex types in PascalCase, so <c>as(Quantity)</c> already matches exactly and needs no
/// alias on any version. Only the primitives, whose FHIR spelling is lower camel case, can be mis-cased
/// at all. Counting capitalized casts is therefore not the same as counting casts that need an alias.
/// </para>
/// <para>
/// <em>The narrowing is not confined to primitives.</em> Ordinal matching applies to every type name, so
/// <c>as(humanname)</c> selects nothing and <c>is resource</c>, <c>is domainresource</c> and
/// <c>as(resource)</c> are false or empty; the resource checks in
/// <see cref="MatchesTypeWithInheritance"/> compare Ordinal against <c>Resource</c> and
/// <c>DomainResource</c> for the same reason. The published spellings are <c>HumanName</c>,
/// <c>Resource</c> and <c>DomainResource</c>, so this is the spec-correct answer - but it is stated here
/// because the primitive argument above does not reach it and would not on its own justify it.
/// <c>TypeNameCaseSensitivityTests</c> pins it.
/// </para>
/// <para>
/// <c>Uri</c> is deliberately separate from the System aliases because <c>System.Uri</c> is not a
/// FHIRPath type. STU3's <c>ConceptMap-source-uri</c> and <c>ConceptMap-target-uri</c> simply misspell
/// FHIR <c>uri</c>. The dedicated erratum alias exists solely to keep those two search parameters
/// indexing; it can be removed with those artifacts or with STU3 support, and must not be extended as
/// though it had the namespace rule's authority.
/// </para>
/// <para>
/// HAPI's implementation leaves an unresolved inconsistency: its <c>funcOfType</c> uses exact
/// <c>equals()</c> without the legacy <c>as</c> hook, so below R5 it is stricter than <c>as</c>. Ignixa
/// retains one matcher for the three cast spellings; that internal consistency is a deliberate choice,
/// not evidence that R5's <c>ofType()</c> sentence authorizes pre-R5 <c>as</c> behaviour.
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
    // System-only types, which an unqualified type TEST must not satisfy from a FHIR value: Patient.active
    // is a FHIR boolean, so `active is Boolean` is false while `exists() is Boolean` is true.
    //
    // Quantity's absence is load-bearing: it exists in both models with identical spelling, so adding it
    // here would make `is Quantity` false on a real FHIR Quantity. Date's absence is inert: FHIR spells
    // it `date`, never `Date`, so `birthDate is Date` already fails the Ordinal comparison in
    // TypeNamesMatch before this set is ever consulted. Neither is present, so the only edit either one
    // admits is an addition: adding Date would change no result, adding Quantity would change many.
    //
    // This gate is TEST-only, and `is` is strict on every version while the casts are gated (see
    // UsesR5TypeRules). That asymmetry is the spec's, not ours: the System/FHIR namespace distinction is
    // stated as an explicit exception for the test operators and predates R5 - FHIR's own documentation
    // demonstrates it with `Patient.name.given.is(System.string).not()` - whereas R5 2.1.9.1.2 withdrew
    // from `as` a latitude R4 had granted it. Nothing was ever withdrawn from `is`, so there is nothing
    // for a version gate here to switch on.
    private static readonly FrozenSet<string> SystemOnlyTypes = new[]
    {
        "Boolean", "Integer", "Decimal", "String", "DateTime", "Time"
    }.ToFrozenSet(StringComparer.Ordinal);

    // The evaluator carries System primitive values with their FHIR-style runtime spellings, so these
    // canonical names also normalize a System value back to its model name in every version. Separately,
    // R4/R4B explicitly allow as() to cross the same names to FHIR primitives below R5. Neither use is
    // permission to compare arbitrary casing.
    private static readonly FrozenDictionary<string, string> CanonicalSystemPrimitiveSpellings =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Boolean"] = "boolean",
            ["Integer"] = "integer",
            ["Decimal"] = "decimal",
            ["String"] = "string",
            ["Date"] = "date",
            ["DateTime"] = "dateTime",
            ["Time"] = "time"
        }.ToFrozenDictionary(StringComparer.Ordinal);

    // Pure STU3 artifact errata, with no System/FHIR namespace basis. Only ConceptMap-source-uri and
    // ConceptMap-target-uri need the entry, but the gate below is the FHIR version rather than the
    // artifact, so as(Uri) also crosses on R4 and R4B where no shipped artifact requires it. That extra
    // breadth is accepted, not intended; it is not licence to add spellings no artifact needs.
    private static readonly FrozenDictionary<string, string> PreR5ArtifactErratumCastAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Uri"] = "uri"
        }.ToFrozenDictionary(StringComparer.Ordinal);

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
    // them and asking the schema about them would wrongly report them as unresolvable. Ordinal is
    // deliberate: the model owns the spelling of its own identifiers, and System names are canonical.
    // Case-insensitive lookup here previously disagreed with SystemOnlyTypes and accepted arbitrary case.
    // One consequence is worth naming: `Long` resolves but `long` does not, and no published model
    // declares a `long` either (R5 and R6 spell the 64-bit integer `integer64`), so `1 is long` throws
    // rather than returning false. See EnsureTypeIdentifierResolves.
    private static readonly FrozenSet<string> SystemTypeNames = new[]
    {
        "Boolean", "String", "Integer", "Long", "Decimal", "Date", "DateTime", "Time", "Quantity"
    }.ToFrozenSet(StringComparer.Ordinal);

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
    /// <para>
    /// <strong>Resolution is deliberately more lenient than matching.</strong> Matching is <c>Ordinal</c>
    /// throughout, but the fall-through to <see cref="ISchema.IsKnownType"/> is backed by the generated
    /// providers' <c>OrdinalIgnoreCase</c> lookup. The two therefore disagree: <c>as(DATETIME)</c>
    /// resolves and then matches nothing, returning empty, while <c>as(long)</c> does not resolve at all
    /// and throws. Read strictly, "if the identifier cannot be resolved ... throw" would make the first
    /// throw as well - <c>DATETIME</c> is not a valid identifier in any FHIR model.
    /// </para>
    /// <para>
    /// Returning empty for a mis-cased identifier that names a real type is a deliberate engine choice,
    /// pinned by <c>TypeNameCaseSensitivityTests</c>, not an accident of the comparer. Tightening
    /// resolution to <c>Ordinal</c> would be the spec-literal answer, but <see cref="ISchema.IsKnownType"/>
    /// is the schema providers' general-purpose type lookup with callers well beyond the type operators,
    /// so narrowing it for this rule alone would change unrelated behaviour across five generated
    /// providers. Casing is also the error this engine can most safely be lenient about: it costs the
    /// caller an empty result rather than a wrong one. Revisiting it is a schema-provider decision, not
    /// one to take here.
    /// </para>
    /// <para>
    /// A name that is neither a System type nor declared by the model throws, and <c>long</c> is the case
    /// most likely to surprise. FHIRPath spells its 64-bit integer <c>Long</c>; no published FHIR model
    /// declares a <c>long</c>, because R5 and R6 spell theirs <c>integer64</c>. So <c>1 is long</c> throws
    /// on every version rather than returning false, which is what the quoted rule requires.
    /// <c>1 is Long</c> resolves and is false against a FHIR value.
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
    /// <c>{Version}SearchParameterDefinitions.g.cs</c>, reading both the top-level <c>expression:</c> and
    /// the <c>SearchParameterComponentInfo</c> expressions; recount there rather than trusting these if it
    /// matters. Every expression in those files is an inline literal - the <c>Constants.Expr_*</c> interning
    /// the generator emits is constraint-side only, in <c>{Version}CoreSchemaProvider.g.cs</c>, and never
    /// appears on the SearchParameter side.) In R5 HL7 rewrote almost every one to <c>ofType()</c>: the
    /// operator survives in eight expressions only -
    /// <c>Bundle.entry[0].resource as Composition</c> and <c>as MessageHeader</c> (indexed, so singletons),
    /// the four <c>ConceptMap.sourceScope</c>/<c>targetScope</c> casts to <c>uri</c> and <c>canonical</c>
    /// (both choice elements are 0..1, so singletons by construction),
    /// <c>NutritionIntake.reported as Reference</c> (0..1), and
    /// <c>AdverseEvent.suspectEntity.instance as Reference</c>, which is the only genuinely repeating one -
    /// see the note below. Enforcing the rule below R5 would make <c>ElementSearchIndexer</c> throw on any
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

    private static bool EnforcesSingletonCast(ISchema? schema) => UsesR5TypeRules(schema);

    /// <summary>
    /// Whether R5's type-operator rules apply: the withdrawal of the System spellings from the casts, and
    /// the singleton-input rule for <c>as</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two rules share this predicate because they share one cause - both arrived in R5 2.1.9.1.2 - not
    /// because either implies the other. The moment one of them moves independently of the other, this
    /// should become two predicates.
    /// </para>
    /// <para>
    /// An absent schema fails open to the pre-R5 behaviour, and so does
    /// <see cref="FhirVersion.Unspecified"/>. That direction is the safe one for both rules: pre-R5
    /// accepts strictly more, so an unknown version can never make a previously valid expression start
    /// returning empty or throwing. <see cref="FhirVersion.Unspecified"/> needs its own test because its
    /// value sorts above <see cref="FhirVersion.R5"/> and its own documentation says it defaults to the
    /// latest version for comparisons - the opposite of what is wanted here.
    /// </para>
    /// </remarks>
    private static bool UsesR5TypeRules(ISchema? schema) =>
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
    public static bool MatchesTypeWithInheritance(
        IElement element,
        string typeName,
        TypeMatchMode mode,
        ISchema? schema)
    {
        var currentType = element.InstanceType;
        if (string.IsNullOrEmpty(currentType))
            return false;

        bool elementIsSystemType = IsSystemElement(element);

        while (!string.IsNullOrEmpty(currentType))
        {
            if (TypeNamesMatch(currentType, typeName, mode, schema, elementIsSystemType))
                return true;

            if (!TryGetBaseType(currentType, mode, out var baseType))
                break;

            currentType = baseType;
        }

        if (element.Type?.Info is { IsResource: true })
        {
            if (typeName.Equals("Resource", StringComparison.Ordinal))
                return true;

            if (typeName.Equals("DomainResource", StringComparison.Ordinal))
            {
                var instanceType = element.InstanceType;
                return !ResourcesNotExtendingDomainResource.Contains(instanceType);
            }
        }

        return false;
    }

    private static bool TypeNamesMatch(
        string instanceTypeName,
        string requestedTypeName,
        TypeMatchMode mode,
        ISchema? schema,
        bool elementIsSystemType)
    {
        if (instanceTypeName.Equals(requestedTypeName, StringComparison.Ordinal))
            return true;

        bool matchesSystemSpelling = MatchesAlias(CanonicalSystemPrimitiveSpellings);

        // A System value carries FHIR's lower camel case spelling in InstanceType, so System.Integer has
        // to reach an integer instance type on every version. This is the System namespace working as
        // specified, not a pre-R5 concession, so it is deliberately above the version gate.
        if (elementIsSystemType && matchesSystemSpelling)
            return true;

        if (mode != TypeMatchMode.Cast || UsesR5TypeRules(schema))
            return false;

        return matchesSystemSpelling || MatchesAlias(PreR5ArtifactErratumCastAliases);

        bool MatchesAlias(FrozenDictionary<string, string> aliases) =>
            aliases.TryGetValue(requestedTypeName, out var fhirTypeName)
            && instanceTypeName.Equals(fhirTypeName, StringComparison.Ordinal);
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
    /// <paramref name="mode"/> selects the operator family, while <paramref name="schema"/> supplies the
    /// version boundary for the pre-R5 cast aliases. See <see cref="TypeMatcher"/> for why both are
    /// required, and <c>UsesR5TypeRules</c> for why an unknown version fails open.
    /// </remarks>
    public static bool IsTypeMatch(
        IElement element,
        string typeName,
        TypeMatchMode mode,
        ISchema? schema)
    {
        var (baseTypeName, isSystemNamespace, isFhirNamespace) = ParseTypeName(typeName);

        if (mode == TypeMatchMode.TypeTest && !NamespaceMatches(element, baseTypeName, isSystemNamespace, isFhirNamespace))
            return false;

        return MatchesTypeWithInheritance(element, baseTypeName, mode, schema);
    }

    /// <summary>
    /// Applies FHIR's System-versus-FHIR namespace distinction, which R5 2.1.9.1.2 scopes to the type
    /// TEST operators and explicitly withholds from the casts.
    /// </summary>
    private static bool NamespaceMatches(IElement element, string baseTypeName, bool isSystemNamespace, bool isFhirNamespace)
    {
        // A FHIRPath literal is a System value; anything sourced from the resource tree is a FHIR value.
        bool elementIsSystemType = IsSystemElement(element);

        if (isSystemNamespace)
            return elementIsSystemType;

        if (isFhirNamespace)
            return !elementIsSystemType;

        // Unqualified and capitalized, e.g. String: the FHIRPath System type, so Patient.active is not a
        // Boolean even though it is a boolean.
        return !SystemOnlyTypes.Contains(baseTypeName) || elementIsSystemType;
    }

    /// <summary>
    /// Whether the element carries a FHIRPath <c>System</c> value rather than a FHIR one.
    /// </summary>
    /// <remarks>
    /// The element declares this by implementing <see cref="ISystemValueElement"/>; it is never inferred.
    /// This used to test whether the implementing class name contained "Primitive", which silently made
    /// the answer depend on which evaluation path produced the value: the interpreter's wrapper is called
    /// <c>PrimitiveElement</c>, the compiler's <c>LiteralElement</c>, so on R5 and later
    /// <c>value.count().ofType(Integer)</c> selected the integer when interpreted and dropped it when
    /// compiled. Reintroducing a name-based guess here reintroduces that divergence.
    /// </remarks>
    private static bool IsSystemElement(IElement element) => element is ISystemValueElement;

    /// <summary>
    /// Filters a collection to the elements matching the specified type, for <c>as</c>, <c>as()</c> and
    /// <c>ofType()</c>.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="TypeMatchMode.Cast"/>: subclass-aware over complex types and resources, exact over
    /// primitives, with only the documented pre-R5 namespace and artifact aliases. That combination is
    /// what lets <c>ofType(Quantity)</c> select a <c>SimpleQuantity</c> while
    /// <c>ofType(string)</c> still rejects a <c>code</c>, arbitrary casing still rejects every primitive,
    /// and STU3's <c>as(DateTime)</c> still selects a <c>dateTime</c>.
    /// </remarks>
    public static IEnumerable<IElement> FilterByType(
        IEnumerable<IElement> elements,
        string typeName,
        ISchema? schema) =>
        elements.Where(e => IsTypeMatch(e, typeName, TypeMatchMode.Cast, schema));
}
