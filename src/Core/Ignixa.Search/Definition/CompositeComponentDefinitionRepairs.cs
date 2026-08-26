// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.Search.Definition;

/// <summary>
/// Component definition references that a shipped FHIR specification points at a
/// <c>SearchParameter</c> it does not publish, together with the parameter it meant.
/// </summary>
/// <remarks>
/// <para>
/// A composite search parameter names its components by canonical URL. When that URL resolves to
/// nothing the component's type is unknown, the indexer skips the whole composite, and the search
/// silently returns an empty bundle with HTTP 200 - so <c>Observation?code-value-quantity=</c> looks
/// like "no matches" rather than "not indexed". Every entry here is a URL the published package
/// genuinely omits, verified against the HL7 package rather than inferred from a failure.
/// </para>
/// <para>
/// Only redirects that are unambiguous belong here. Each one is a URL whose intended parameter is
/// identified by the specification's own naming - the STU3 entries all point at
/// <c>Observation-code</c>, which STU3 publishes under the multi-resource <c>clinical-code</c> URL
/// with the same code, the same <c>token</c> type and an expression that includes
/// <c>Observation.code</c>. Nothing is guessed by matching codes at runtime: a heuristic that bound
/// whatever parameter happened to share a code would silently bind the wrong one the first time two
/// parameters collided, which is the failure mode this table exists to remove rather than relocate.
/// </para>
/// <para>
/// <c>microsoft/fhir-server</c> solves the same problem by curating its embedded
/// <c>search-parameters.json</c> - its STU3 bundle carries these four composites with the component
/// definition already rewritten to <c>clinical-code</c>. Ignixa generates its definitions from the
/// published packages instead, so the repair lives next to the loader rather than in the data.
/// </para>
/// <para>
/// Dangling references that this table does not repair are left unresolved deliberately, and
/// <c>Ignixa.Search.Tests</c>' composite component census is where each one is recorded with its
/// reason.
/// </para>
/// </remarks>
internal static class CompositeComponentDefinitionRepairs
{
    private const string ClinicalCode = "http://hl7.org/fhir/SearchParameter/clinical-code";
    private const string Stu3ObservationCode = "http://hl7.org/fhir/SearchParameter/Observation-code";

    /// <summary>
    /// The redirects, keyed by version and by the component's URL as written.
    /// </summary>
    /// <remarks>
    /// The key half is a <see langword="string"/> rather than the <see cref="Uri"/> the caller passes,
    /// deliberately: <see cref="Uri"/> equality is case-insensitive on scheme and host and ignores the
    /// fragment, which is looseness this table must not have against package data - a repair is a claim
    /// about one exact canonical URL a package publishes, and matching a near-miss would rewrite a
    /// reference that was never the one meant. <c>ValueTuple</c> equality uses
    /// <c>EqualityComparer&lt;string&gt;.Default</c>, so the comparison is ordinal; a tuple key cannot
    /// take a <see cref="StringComparer"/>, which is why that is stated here rather than passed.
    /// </remarks>
    private static readonly IReadOnlyDictionary<(FhirVersion Version, string DefinitionUrl), Uri> Redirects =
        new Dictionary<(FhirVersion, string), Uri>
        {
            // STU3 publishes the Observation 'code' parameter only under the multi-resource
            // clinical-code URL, while all four Observation-code-value-* composites reference a
            // standalone Observation-code that hl7.fhir.r3.core#3.0.2 does not contain. Without this,
            // STU3 indexes none of code-value-concept, code-value-date, code-value-quantity or
            // code-value-string.
            [(FhirVersion.Stu3, Stu3ObservationCode)] = new Uri(ClinicalCode),
        };

    /// <summary>
    /// Returns the URL the component reference meant, or <paramref name="definitionUrl"/> when this
    /// version has no repair for it.
    /// </summary>
    public static Uri Resolve(FhirVersion version, Uri definitionUrl)
    {
        ArgumentNullException.ThrowIfNull(definitionUrl);

        return Redirects.TryGetValue((version, definitionUrl.OriginalString), out Uri repaired)
            ? repaired
            : definitionUrl;
    }

    /// <summary>
    /// The repairs defined for a version, so the census can assert each one is still needed and still
    /// lands on a parameter that exists.
    /// </summary>
    public static IEnumerable<(FhirVersion Version, string DefinitionUrl, Uri RepairedUrl)> All =>
        Redirects.Select(entry => (entry.Key.Version, entry.Key.DefinitionUrl, entry.Value));
}
