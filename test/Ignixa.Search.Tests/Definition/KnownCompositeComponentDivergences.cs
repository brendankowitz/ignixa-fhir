using Ignixa.Abstractions;

namespace Ignixa.Search.Tests.Definition;

/// <summary>
/// Every composite component the shipped definitions cannot resolve, and why each one stays that way.
/// </summary>
/// <remarks>
/// <para>
/// All of these are dangling references in the published HL7 packages: the package ships a composite
/// whose component names a canonical URL that the same package never publishes as a
/// <c>SearchParameter</c>. That was verified against <c>hl7.fhir.r5.core#5.0.0</c> and
/// <c>hl7.fhir.r6.core#6.0.0-ballot2</c> directly, not inferred from the failure. Ignixa's generated
/// definitions faithfully reproduce the packages, so the dangling reference arrives with the data.
/// </para>
/// <para>
/// The consequence is uniform and worth stating plainly: each composite listed here indexes nothing,
/// so a search on it returns an empty bundle with HTTP 200 rather than an error. That is the same
/// shape of failure as an unindexed parameter, and it is why <c>Observation-code-value-*</c> under
/// STU3 was repaired in <c>CompositeComponentDefinitionRepairs</c> instead of being listed here.
/// </para>
/// <para>
/// Repairing the rest is not the same kind of edit, and upstream takes no single position on them.
/// <c>Observation-code-value-string</c> under R5 needs the search parameter the package deleted to be
/// reintroduced, which changes R5's supported parameter surface - and that is what
/// <c>microsoft/fhir-server</c> does. It drops the <c>ResearchStudy</c>, <c>Ingredient</c> and
/// <c>Encounter-location-period</c> composites from its curated bundle entirely; it carries the
/// <c>TestScript-scope-artifact-*</c> group but names it in a hard-coded <c>_missingExpressionsInR5</c>
/// exclusion list; and it <em>repoints</em> <c>DeviceDefinition-specification-version</c>'s component at
/// <c>CanonicalResource-version</c>. No R6 row has an upstream position at all, because
/// <c>microsoft/fhir-server</c> ships no R6 bundle. Each is a product decision rather than a defect fix,
/// so each entry records what upstream did and leaves the call deliberate. Read the entry rather than
/// this summary for any particular row.
/// </para>
/// </remarks>
internal static class KnownCompositeComponentDivergences
{
    private const string R5SpecOmission =
        """
        hl7.fhir.r5.core#5.0.0 ships this composite but does not publish the component's
        SearchParameter, so the reference dangles in the package itself.
        """;

    private const string R6SpecOmission =
        """
        hl7.fhir.r6.core#6.0.0-ballot2 ships this composite but does not publish the component's
        SearchParameter, so the reference dangles in the package itself.
        """;

    /// <summary>
    /// The boilerplate every entry's reason opens with, so a census can measure what the entry adds on
    /// top of it rather than what it inherits.
    /// </summary>
    /// <remarks>
    /// Each of these is around 140 characters - more than any plausible floor on its own - so an entry
    /// reduced to nothing but its preamble still passes a check that measures <c>Reason</c> whole. The
    /// substantive-reason census strips these first for that reason.
    /// </remarks>
    public static IReadOnlyList<string> SharedPreambles { get; } = [R5SpecOmission, R6SpecOmission];

    public static IReadOnlyList<CompositeComponentDivergence> All { get; } =
    [
        new(
            FhirVersion.R5,
            "http://hl7.org/fhir/SearchParameter/Observation-code-value-string",
            1,
            "http://hl7.org/fhir/SearchParameter/Observation-value-string",
            $"""
             {R5SpecOmission}
             R5 removed Observation-value-string while keeping Observation-code-value-string, so
             Observation?code-value-string= indexes nothing under R5. R4 and R4B are unaffected -
             both publish the parameter - and microsoft/fhir-server closes this by reintroducing
             Observation-value-string with expression 'value.ofType(string)' in its curated R5
             bundle. Doing the same here would add a search parameter to R5's supported surface,
             which is a product decision rather than a repair of a dangling reference.
             """),
        new(
            FhirVersion.R5,
            "http://hl7.org/fhir/SearchParameter/Encounter-location-period",
            1,
            "http://hl7.org/fhir/SearchParameter/Encounter-period",
            $"""
             {R5SpecOmission}
             R5 renamed Encounter.period to Encounter.actualPeriod and dropped Encounter-period,
             leaving Encounter-location-period pointing at the removed parameter.
             microsoft/fhir-server does not carry this composite at all.
             """),
        new(
            FhirVersion.R5,
            "http://hl7.org/fhir/SearchParameter/ResearchStudy-progress-status-state-actual",
            0,
            "http://hl7.org/fhir/SearchParameter/ResearchStudy-state",
            $"{R5SpecOmission} microsoft/fhir-server does not carry this composite at all."),
        new(
            FhirVersion.R5,
            "http://hl7.org/fhir/SearchParameter/ResearchStudy-progress-status-state-actual",
            1,
            "http://hl7.org/fhir/SearchParameter/ResearchStudy-actual",
            $"{R5SpecOmission} microsoft/fhir-server does not carry this composite at all."),
        new(
            FhirVersion.R5,
            "http://hl7.org/fhir/SearchParameter/ResearchStudy-progress-status-state-period",
            0,
            "http://hl7.org/fhir/SearchParameter/ResearchStudy-state",
            $"{R5SpecOmission} microsoft/fhir-server does not carry this composite at all."),
        new(
            FhirVersion.R5,
            "http://hl7.org/fhir/SearchParameter/ResearchStudy-progress-status-state-period",
            1,
            "http://hl7.org/fhir/SearchParameter/ResearchStudy-period",
            $"{R5SpecOmission} microsoft/fhir-server does not carry this composite at all."),
        new(
            FhirVersion.R5,
            "http://hl7.org/fhir/SearchParameter/ResearchStudy-progress-status-state-period-actual",
            0,
            "http://hl7.org/fhir/SearchParameter/ResearchStudy-state",
            $"{R5SpecOmission} microsoft/fhir-server does not carry this composite at all."),
        new(
            FhirVersion.R5,
            "http://hl7.org/fhir/SearchParameter/ResearchStudy-progress-status-state-period-actual",
            1,
            "http://hl7.org/fhir/SearchParameter/ResearchStudy-period",
            $"{R5SpecOmission} microsoft/fhir-server does not carry this composite at all."),
        new(
            FhirVersion.R5,
            "http://hl7.org/fhir/SearchParameter/ResearchStudy-progress-status-state-period-actual",
            2,
            "http://hl7.org/fhir/SearchParameter/ResearchStudy-actual",
            $"{R5SpecOmission} microsoft/fhir-server does not carry this composite at all."),
        new(
            FhirVersion.R5,
            "http://hl7.org/fhir/SearchParameter/TestScript-scope-artifact-conformance",
            0,
            "http://hl7.org/fhir/SearchParameter/TestScript-artifact",
            $"""
             {R5SpecOmission}
             microsoft/fhir-server carries this composite but names it in the hard-coded
             _missingExpressionsInR5 exclusion list in SearchParameterDefinitionBuilder, because its
             own definition builder throws InvalidDefinitionException on an unresolvable component
             rather than skipping at index time. R6 publishes TestScript-artifact, so this divergence
             is R5-only and disappears on its own there.
             """),
        new(
            FhirVersion.R5,
            "http://hl7.org/fhir/SearchParameter/TestScript-scope-artifact-conformance",
            1,
            "http://hl7.org/fhir/SearchParameter/TestScript-conformance",
            $"{R5SpecOmission} Excluded by name in microsoft/fhir-server's _missingExpressionsInR5 list; published in R6."),
        new(
            FhirVersion.R5,
            "http://hl7.org/fhir/SearchParameter/TestScript-scope-artifact-phase",
            0,
            "http://hl7.org/fhir/SearchParameter/TestScript-artifact",
            $"{R5SpecOmission} Excluded by name in microsoft/fhir-server's _missingExpressionsInR5 list; published in R6."),
        new(
            FhirVersion.R5,
            "http://hl7.org/fhir/SearchParameter/TestScript-scope-artifact-phase",
            1,
            "http://hl7.org/fhir/SearchParameter/TestScript-phase",
            $"{R5SpecOmission} Excluded by name in microsoft/fhir-server's _missingExpressionsInR5 list; published in R6."),
        new(
            FhirVersion.R5,
            "http://hl7.org/fhir/SearchParameter/DeviceDefinition-specification-version",
            1,
            "http://hl7.org/fhir/SearchParameter/DeviceDefinition-version",
            $"""
             {R5SpecOmission}
             microsoft/fhir-server repoints this component at CanonicalResource-version in its curated
             R5 bundle. That substitution is a judgement about which parameter the specification meant
             rather than a naming equivalence, so it is recorded rather than copied.
             """),
        new(
            FhirVersion.R5,
            "http://hl7.org/fhir/SearchParameter/Ingredient-strength-concentration-ratio",
            0,
            "http://hl7.org/fhir/SearchParameter/Ingredient-numerator",
            $"{R5SpecOmission} microsoft/fhir-server does not carry this composite at all."),
        new(
            FhirVersion.R5,
            "http://hl7.org/fhir/SearchParameter/Ingredient-strength-concentration-ratio",
            1,
            "http://hl7.org/fhir/SearchParameter/Ingredient-denominator",
            $"{R5SpecOmission} microsoft/fhir-server does not carry this composite at all."),
        new(
            FhirVersion.R5,
            "http://hl7.org/fhir/SearchParameter/Ingredient-strength-presentation-ratio",
            0,
            "http://hl7.org/fhir/SearchParameter/Ingredient-numerator",
            $"{R5SpecOmission} microsoft/fhir-server does not carry this composite at all."),
        new(
            FhirVersion.R5,
            "http://hl7.org/fhir/SearchParameter/Ingredient-strength-presentation-ratio",
            1,
            "http://hl7.org/fhir/SearchParameter/Ingredient-denominator",
            $"{R5SpecOmission} microsoft/fhir-server does not carry this composite at all."),
        new(
            FhirVersion.R6,
            "http://hl7.org/fhir/SearchParameter/ResearchStudy-progress-status-state-actual",
            0,
            "http://hl7.org/fhir/SearchParameter/ResearchStudy-state",
            $"{R6SpecOmission} microsoft/fhir-server ships no R6 bundle, so there is no upstream position to follow."),
        new(
            FhirVersion.R6,
            "http://hl7.org/fhir/SearchParameter/ResearchStudy-progress-status-state-actual",
            1,
            "http://hl7.org/fhir/SearchParameter/ResearchStudy-actual",
            $"{R6SpecOmission} microsoft/fhir-server ships no R6 bundle, so there is no upstream position to follow."),
        new(
            FhirVersion.R6,
            "http://hl7.org/fhir/SearchParameter/ResearchStudy-progress-status-state-period",
            0,
            "http://hl7.org/fhir/SearchParameter/ResearchStudy-state",
            $"{R6SpecOmission} microsoft/fhir-server ships no R6 bundle, so there is no upstream position to follow."),
        new(
            FhirVersion.R6,
            "http://hl7.org/fhir/SearchParameter/ResearchStudy-progress-status-state-period",
            1,
            "http://hl7.org/fhir/SearchParameter/ResearchStudy-period",
            $"{R6SpecOmission} microsoft/fhir-server ships no R6 bundle, so there is no upstream position to follow."),
        new(
            FhirVersion.R6,
            "http://hl7.org/fhir/SearchParameter/ResearchStudy-progress-status-state-period-actual",
            0,
            "http://hl7.org/fhir/SearchParameter/ResearchStudy-state",
            $"{R6SpecOmission} microsoft/fhir-server ships no R6 bundle, so there is no upstream position to follow."),
        new(
            FhirVersion.R6,
            "http://hl7.org/fhir/SearchParameter/ResearchStudy-progress-status-state-period-actual",
            1,
            "http://hl7.org/fhir/SearchParameter/ResearchStudy-period",
            $"{R6SpecOmission} microsoft/fhir-server ships no R6 bundle, so there is no upstream position to follow."),
        new(
            FhirVersion.R6,
            "http://hl7.org/fhir/SearchParameter/ResearchStudy-progress-status-state-period-actual",
            2,
            "http://hl7.org/fhir/SearchParameter/ResearchStudy-actual",
            $"{R6SpecOmission} microsoft/fhir-server ships no R6 bundle, so there is no upstream position to follow."),
        new(
            FhirVersion.R6,
            "http://hl7.org/fhir/SearchParameter/Ingredient-strength-concentration-ratio",
            0,
            "http://hl7.org/fhir/SearchParameter/Ingredient-numerator",
            $"{R6SpecOmission} microsoft/fhir-server ships no R6 bundle, so there is no upstream position to follow."),
        new(
            FhirVersion.R6,
            "http://hl7.org/fhir/SearchParameter/Ingredient-strength-concentration-ratio",
            1,
            "http://hl7.org/fhir/SearchParameter/Ingredient-denominator",
            $"{R6SpecOmission} microsoft/fhir-server ships no R6 bundle, so there is no upstream position to follow."),
        new(
            FhirVersion.R6,
            "http://hl7.org/fhir/SearchParameter/Ingredient-strength-presentation-ratio",
            0,
            "http://hl7.org/fhir/SearchParameter/Ingredient-numerator",
            $"{R6SpecOmission} microsoft/fhir-server ships no R6 bundle, so there is no upstream position to follow."),
        new(
            FhirVersion.R6,
            "http://hl7.org/fhir/SearchParameter/Ingredient-strength-presentation-ratio",
            1,
            "http://hl7.org/fhir/SearchParameter/Ingredient-denominator",
            $"{R6SpecOmission} microsoft/fhir-server ships no R6 bundle, so there is no upstream position to follow."),
        new(
            FhirVersion.R6,
            "http://hl7.org/fhir/SearchParameter/Device-version-type",
            1,
            "http://hl7.org/fhir/SearchParameter/Device-value",
            $"{R6SpecOmission} microsoft/fhir-server ships no R6 bundle, so there is no upstream position to follow."),
        new(
            FhirVersion.R6,
            "http://hl7.org/fhir/SearchParameter/DeviceDefinition-version-type",
            1,
            "http://hl7.org/fhir/SearchParameter/DeviceDefinition-value",
            $"{R6SpecOmission} microsoft/fhir-server ships no R6 bundle, so there is no upstream position to follow."),
    ];
}
