/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * The accepted Firely 5.11.4 / Ignixa divergences, pinned so a new one fails rather than lengthening
 * a document nobody re-reads.
 *
 * Every entry here has a matching row in docs/features/fhirpath/firely-parity.md giving the spec
 * citation and what the seam has to do about it. Adding an entry without adding the row defeats the
 * purpose of the harness.
 */

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// Divergences the inventory has already accounted for, keyed by expression and outcome shape.
/// </summary>
/// <remarks>
/// The value is the number of subject resources the divergence is observed on, which doubles as its
/// blast radius: a behaviour that fires on every resource is a different problem from one that needs
/// a specific element to be present.
/// </remarks>
internal static class KnownDivergences
{
    /// <summary>
    /// Divergences reachable from a shipped R4 SearchParameter expression - the ones that change
    /// search index content on every write, and therefore the only ones that carry a real cost.
    /// </summary>
    public static IReadOnlyDictionary<string, int> SearchParameterSignatures { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            // hasExtension() is unimplemented in both engines; they differ only in when they give up.
            // Firely fails at compile time, so this parameter throws for every resource; Ignixa fails
            // lazily, so it returns empty until a QuestionnaireResponse with items actually reaches
            // the call. On the QuestionnaireResponse subject both throw, which is why the count is 4
            // and not 5.
            ["QuestionnaireResponse.item.where(hasExtension('http://hl7.org/fhir/StructureDefinition/questionnaireresponse-isSubject')).answer.value.ofType(Reference) :: firely=threw:ArgumentException ignixa=empty"] = 4,

            // Firely types every backbone element as "BackboneElement"; Ignixa names it after its path
            // ("Observation.Component"). Same elements, same count, same values - only InstanceType
            // differs, and any converter that switches on it needs to know.
            ["Observation.component :: firely=3 result(s): [BACKBONEELEMENT|null|<null>, BACKBONEELEMENT|null|<null>, BACKBONEELEMENT|null|<null>] ignixa=3 result(s): [OBSERVATION.COMPONENT|null|<null>, OBSERVATION.COMPONENT|null|<null>, OBSERVATION.COMPONENT|null|<null>]"] = 1,
            ["Observation | Observation.component :: firely=4 result(s): [OBSERVATION|null|<null>, BACKBONEELEMENT|null|<null>, BACKBONEELEMENT|null|<null>, BACKBONEELEMENT|null|<null>] ignixa=4 result(s): [OBSERVATION|null|<null>, OBSERVATION.COMPONENT|null|<null>, OBSERVATION.COMPONENT|null|<null>, OBSERVATION.COMPONENT|null|<null>]"] = 1,
        };

    /// <summary>
    /// The population the shipped R4 SearchParameter sweep is expected to produce.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The divergence pins above say what the two engines disagree on and nothing about what they
    /// agreed on or how often. Mutual throws are live in this corpus - the single one counted here is
    /// the <c>hasExtension()</c> subject that makes the pin above 4 and not 5 - so an evaluation that
    /// stops comparing values and starts throwing on both sides leaves every pin above satisfied.
    /// These four numbers are what makes that visible.
    /// </para>
    /// <para>
    /// The shape they expose is worth reading before quoting this sweep as evidence: of 6,835
    /// evaluations per engine, 6,752 agree on empty and only 76 compare matching non-empty values. The
    /// corpus is the shipped R4 SearchParameter expressions run against five subject resources, so
    /// almost every expression addresses a resource type the subject is not. This sweep is a
    /// regression net over the expressions production evaluates on every write, not a broad
    /// conformance measurement - <c>ResourceBackedKnownDivergences</c> is where the volume of matched
    /// values lives, at 10,074.
    /// </para>
    /// </remarks>
    public static ExpressionCorpusExpectations SearchParameterPopulation { get; } =
        new(
            MinimumEvaluationsPerEngine: 6835,
            ExpectedBothThrew: 1,
            ExpectedBothEmpty: 6752,
            MinimumAgreementsOnValues: 76);

    /// <summary>
    /// The population the changed-construct sweep is expected to produce.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 19 of these evaluations are mutual throws - the construct corpus deliberately probes
    /// operations one engine or the other does not implement - and every one of them was uncounted
    /// until this pin existed, since a mutual throw satisfies <see cref="ParityOutcome.Matches"/> and
    /// never becomes a divergence.
    /// </para>
    /// <para>
    /// Two of the 19 are the type-error semantics this branch introduced: <c>birthDate &lt;
    /// '1980-01-01'</c> (String against Date) and <c>birthDate &lt; @T10:30:00</c> (time of day against
    /// a calendar value). They moved the count from 17 to 19 when they were added, which is the evidence
    /// that Firely throws on them too - previously that was only asserted in a comment. A mutual throw
    /// is normally coverage lost; here it is the agreement being measured, so these two are the one case
    /// in this population where a rise was the intended outcome rather than something to investigate.
    /// </para>
    /// <para>
    /// Those two expressions contribute ten evaluations, not two: the sweep runs every expression
    /// against all five subjects and only the Patient has a <c>birthDate</c>. On the other four
    /// <c>birthDate</c> is empty, and FHIRPath's empty propagation short-circuits the comparison before
    /// either engine reaches its type check, so they land in <see cref="ExpectedBothEmpty"/> - which is
    /// why that figure moved by eight at the same time.
    /// </para>
    /// </remarks>
    public static ExpressionCorpusExpectations ConstructPopulation { get; } =
        new(
            MinimumEvaluationsPerEngine: 425,
            ExpectedBothThrew: 19,
            ExpectedBothEmpty: 174,
            MinimumAgreementsOnValues: 174);

    /// <summary>
    /// Divergences in the language constructs this branch changed. None of these is reachable from a
    /// shipped R4 SearchParameter expression, which is what makes them cheap.
    /// </summary>
    public static IReadOnlyDictionary<string, int> ConstructSignatures { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            // Ignixa carries the timezone extremes (+14:00 / -12:00) that make a boundary an actual
            // instant; Firely returns a local-form dateTime with no offset. Same date and time, only
            // the offset differs.
            ["birthDate.lowBoundary() :: firely=1 result(s): [DATETIME|temporal:System.DateTime|1974-12-25T00:00:00] ignixa=1 result(s): [DATETIME|string|1974-12-25T00:00:00.000+14:00]"] = 1,
            ["birthDate.highBoundary() :: firely=1 result(s): [DATETIME|temporal:System.DateTime|1974-12-25T23:59:59.999] ignixa=1 result(s): [DATETIME|string|1974-12-25T23:59:59.999-12:00]"] = 1,
            ["@2012.lowBoundary() :: firely=1 result(s): [DATETIME|temporal:System.DateTime|2012-01-01T00:00:00] ignixa=1 result(s): [DATETIME|string|2012-01-01T00:00:00.000+14:00]"] = 5,
            ["@2012.highBoundary() :: firely=1 result(s): [DATETIME|temporal:System.DateTime|2012-12-31T23:59:59.999] ignixa=1 result(s): [DATETIME|string|2012-12-31T23:59:59.999-12:00]"] = 5,

            // Numerically equal, different decimal scale. Ignixa pads to its working scale where
            // Firely reports the significant digits the spec's boundary rules produce.
            ["1.5.lowBoundary() :: firely=1 result(s): [DECIMAL|decimal|1.45] ignixa=1 result(s): [DECIMAL|decimal|1.45000000]"] = 5,
            ["1.587.highBoundary() :: firely=1 result(s): [DECIMAL|decimal|1.5875] ignixa=1 result(s): [DECIMAL|decimal|1.58750000]"] = 5,
            ["2.0 'cm' * 2.0 'm' :: firely=1 result(s): [QUANTITY|quantity|0.04000000 'm2'] ignixa=1 result(s): [QUANTITY|quantity|0.040000 'm2']"] = 5,

            // Values agree, but carrier-aware comparison exposes that Firely returns temporal values
            // while Ignixa's arithmetic currently returns strings typed as date.
            ["birthDate + 1 year :: firely=1 result(s): [DATE|temporal:System.Date|1975-12-25] ignixa=1 result(s): [DATE|string|1975-12-25]"] = 1,
            ["birthDate - 1 month :: firely=1 result(s): [DATE|temporal:System.Date|1974-11-25] ignixa=1 result(s): [DATE|string|1974-11-25]"] = 1,
            ["birthDate + 1 day :: firely=1 result(s): [DATE|temporal:System.Date|1974-12-26] ignixa=1 result(s): [DATE|string|1974-12-26]"] = 1,
            ["birthDate + 30 days :: firely=1 result(s): [DATE|temporal:System.Date|1975-01-24] ignixa=1 result(s): [DATE|string|1975-01-24]"] = 1,
            ["birthDate + 1 week :: firely=1 result(s): [DATE|temporal:System.Date|1975-01-01] ignixa=1 result(s): [DATE|string|1975-01-01]"] = 1,
            ["@2012-01-01 + 1 year :: firely=1 result(s): [DATE|temporal:System.Date|2013-01-01] ignixa=1 result(s): [DATE|string|2013-01-01]"] = 5,
            ["@2012-01-31 + 1 month :: firely=1 result(s): [DATE|temporal:System.Date|2012-02-29] ignixa=1 result(s): [DATE|string|2012-02-29]"] = 5,
            ["@2012-02-29 + 1 year :: firely=1 result(s): [DATE|temporal:System.Date|2013-02-28] ignixa=1 result(s): [DATE|string|2013-02-28]"] = 5,

            // Ignixa implements time + quantity; Firely 5.11.4 throws on it. 10:30 + 1 hour = 11:30,
            // which is what Ignixa returns.
            ["@T10:30:00 + 1 hour :: firely=threw:InvalidOperationException ignixa=1 result(s): [TIME|string|11:30:00]"] = 5,

            // "in" with an empty left operand: the spec says empty, Firely says false. Fires on the four
            // non-Patient subjects, where `gender` does not exist; on Patient itself the left operand is
            // present and both engines agree, which is why the count is 4 and not 5.
            ["gender in ('male' | 'female') :: firely=1 result(s): [BOOLEAN|boolean|false] ignixa=empty"] = 4,

            // Unary minus applied to a path rather than a literal: Ignixa negates, Firely gives up.
            // multipleBirthInteger is 2 on the Patient fixture, so -2 is the correct negation.
            ["- multipleBirthInteger :: firely=empty ignixa=1 result(s): [INTEGER|integer|-2]"] = 1,

            // Ignixa resolves the type-suffixed choice element name; Firely only exposes the base
            // name, so the suffixed path is empty for it. deceasedBoolean is false on the Patient
            // fixture, so `false` is the correct value.
            ["deceasedBoolean as boolean :: firely=empty ignixa=1 result(s): [BOOLEAN|boolean|false]"] = 1,

        };
}
