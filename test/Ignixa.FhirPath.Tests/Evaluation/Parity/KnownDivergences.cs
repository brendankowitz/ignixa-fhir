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
            ["Observation.component :: firely=3 result(s) ignixa=3 result(s)"] = 1,
            ["Observation | Observation.component :: firely=4 result(s) ignixa=4 result(s)"] = 1,
        };

    /// <summary>
    /// Divergences in the language constructs this branch changed. None of these is reachable from a
    /// shipped R4 SearchParameter expression, which is what makes them cheap.
    /// </summary>
    public static IReadOnlyDictionary<string, int> ConstructSignatures { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            // Ignixa carries the timezone extremes (+14:00 / -12:00) that make a boundary an actual
            // instant; Firely returns a local-form dateTime with no offset.
            ["birthDate.lowBoundary() :: firely=1 result(s) ignixa=1 result(s)"] = 1,
            ["birthDate.highBoundary() :: firely=1 result(s) ignixa=1 result(s)"] = 1,
            ["@2012.lowBoundary() :: firely=1 result(s) ignixa=1 result(s)"] = 5,

            // Same offset difference, but this one also carries a genuine Ignixa defect: the high
            // boundary of the year 2012 is 2012-12-31, and Ignixa answers 2012-01-31. Month-precision
            // input (@2012-06) is correct, so the bug is specific to year precision.
            ["@2012.highBoundary() :: firely=1 result(s) ignixa=1 result(s)"] = 5,

            // Numerically equal, different decimal scale. Ignixa pads to its working scale where
            // Firely reports the significant digits the spec's boundary rules produce.
            ["1.5.lowBoundary() :: firely=1 result(s) ignixa=1 result(s)"] = 5,
            ["1.587.highBoundary() :: firely=1 result(s) ignixa=1 result(s)"] = 5,
            ["2.0 'cm' * 2.0 'm' :: firely=1 result(s) ignixa=1 result(s)"] = 5,

            // Ignixa implements time + quantity; Firely 5.11.4 throws on it.
            ["@T10:30:00 + 1 hour :: firely=threw:InvalidOperationException ignixa=1 result(s)"] = 5,

            // "in" with an empty left operand: the spec says empty, Firely says false.
            ["gender in ('male' | 'female') :: firely=1 result(s) ignixa=empty"] = 4,

            // Unary minus applied to a path rather than a literal: Ignixa negates, Firely gives up.
            ["- multipleBirthInteger :: firely=empty ignixa=1 result(s)"] = 1,

            // Ignixa resolves the type-suffixed choice element name; Firely only exposes the base
            // name, so the suffixed path is empty for it.
            ["deceasedBoolean as boolean :: firely=empty ignixa=1 result(s)"] = 1,

        };
}
