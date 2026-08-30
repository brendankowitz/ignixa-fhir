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
/// A composite names its components by canonical URL. When that URL resolves to nothing the component's
/// type is unknown, the indexer skips the whole composite, and the search returns an empty bundle with
/// HTTP 200 - <c>Observation?code-value-quantity=</c> looks like "no matches" rather than "not indexed".
/// </para>
/// <para>
/// Every entry is a URL the published package genuinely omits, verified against the HL7 package and
/// redirected to the parameter the specification's own naming identifies. Nothing is matched by code at
/// runtime: a heuristic binding whatever parameter shared a code would silently bind the wrong one the
/// first time two collided, relocating the failure this table exists to remove. References this table
/// does not repair stay unresolved deliberately, recorded with reasons by <c>Ignixa.Search.Tests</c>'
/// composite component census. (<c>microsoft/fhir-server</c> instead curates its embedded
/// <c>search-parameters.json</c>; Ignixa generates from the packages, so the repair sits by the loader.)
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
    /// The key half is a <see langword="string"/> rather than the <see cref="Uri"/> the caller passes:
    /// <see cref="Uri"/> equality is case-insensitive on scheme and host and ignores the fragment, and a
    /// repair is a claim about one exact canonical URL. <c>ValueTuple</c> equality uses
    /// <c>EqualityComparer&lt;string&gt;.Default</c>, so the comparison is ordinal; a tuple key cannot take
    /// a <see cref="StringComparer"/>, which is why that is stated here rather than passed.
    /// </remarks>
    private static readonly IReadOnlyDictionary<(FhirVersion Version, string DefinitionUrl), Uri> Redirects =
        new Dictionary<(FhirVersion, string), Uri>
        {
            // STU3 publishes the Observation 'code' parameter only under the multi-resource clinical-code
            // URL, while all four Observation-code-value-* composites reference a standalone
            // Observation-code that hl7.fhir.r3.core#3.0.2 does not contain.
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
