// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Abstractions;

namespace Ignixa.Search.Definition;

/// <summary>
/// Shipped composite search parameters whose component expressions select elements that index as another kind than the
/// component declares, so no query could ever match them, after <see cref="CompositeComponentDefinitionRepairs"/>.
/// </summary>
/// <remarks>
/// A static table rather than a check at load or first use: typing the expressions needs a FHIRPath analyzer over the
/// schema, which costs about half a second, and the shipped definitions only change when they are regenerated.
/// <c>Ignixa.Search.Tests</c>' consistency census runs <see cref="CompositeComponentConsistency"/> over every shipped
/// version and fails unless its findings equal this table exactly, so a regenerated definition set cannot drift from it.
/// Composites registered later (from packages, through <see cref="SearchParameterDefinitionBuilder"/>) are not checked.
/// <para>
/// Only the R6 <c>TestScript-scope-artifact-*</c> entries reach the in-memory interpreter's check. For the others the
/// table is a backstop: the binder refuses <c>Composition-section-code-text</c> because its second component is
/// <c>Special</c>, and the options builder records R5 <c>Encounter-location-period</c> as unsupported because a component
/// does not resolve.
/// </para>
/// </remarks>
internal static class KnownInconsistentComposites
{
    private const string SectionText =
        "component 1 ('section.text') is declared Special and selects a Narrative, which no converter indexes, so the composite is never indexed";

    /// <summary>The known-inconsistent composites by version and canonical URL (ordinal), with why each cannot match.</summary>
    public static IReadOnlyDictionary<(FhirVersion Version, string Url), string> All { get; } =
        new Dictionary<(FhirVersion, string), string>
        {
            [(FhirVersion.R5, "http://hl7.org/fhir/SearchParameter/Composition-section-code-text")] = SectionText,
            [(FhirVersion.R6, "http://hl7.org/fhir/SearchParameter/Composition-section-code-text")] = SectionText,
            [(FhirVersion.R5, "http://hl7.org/fhir/SearchParameter/Encounter-location-period")] =
                "component 0 ('location.reference') is declared Reference but selects the reference string, which indexes as a String",
            [(FhirVersion.R6, "http://hl7.org/fhir/SearchParameter/TestScript-scope-artifact-conformance")] =
                "component 0 ('artifact') is declared Reference but selects a canonical, which indexes as a Uri",
            [(FhirVersion.R6, "http://hl7.org/fhir/SearchParameter/TestScript-scope-artifact-phase")] =
                "component 0 ('artifact') is declared Reference but selects a canonical, which indexes as a Uri",
        };

    /// <summary>Why the composite at <paramref name="url"/> cannot match in <paramref name="version"/>, or null when it is not listed.</summary>
    public static string? For(FhirVersion version, Uri? url) =>
        url is not null && All.TryGetValue((version, url.OriginalString), out string? reason) ? reason : null;
}
