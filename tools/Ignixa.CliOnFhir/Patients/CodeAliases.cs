// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>Short names for the code systems clinical data uses most, so a patient map writes <c>sct|44054006</c>.</summary>
internal static class CodeAliases
{
    private const int MaxSystemLength = 256;

    private static readonly IReadOnlyDictionary<string, string> ByUri = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["http://snomed.info/sct"] = "sct",
        ["http://loinc.org"] = "loinc",
        ["http://www.nlm.nih.gov/research/umls/rxnorm"] = "rxnorm",
        ["http://hl7.org/fhir/sid/cvx"] = "cvx",
        ["http://hl7.org/fhir/sid/icd-10-cm"] = "icd10cm",
        ["http://hl7.org/fhir/sid/icd-10"] = "icd10",
        ["http://www.ama-assn.org/go/cpt"] = "cpt",
        ["http://hl7.org/fhir/sid/ndc"] = "ndc",
        ["http://unitsofmeasure.org"] = "ucum",
    };

    /// <summary>
    /// The alias of <paramref name="system"/>; else the system itself when it is shaped as a URI, else its digest, so text
    /// posing as a system (a sentence, say) never reaches a map.
    /// </summary>
    public static string Of(string system) =>
        ByUri.TryGetValue(system, out string? alias) ? alias
        : system.Length <= MaxSystemLength && !system.Any(char.IsWhiteSpace) && system.Contains(':', StringComparison.Ordinal) ? system
        : FhirIds.Digest(system);

    /// <summary>A <c>system|code</c> key with its system aliased (<see cref="Of"/>).</summary>
    public static string Shorten(string key)
    {
        int bar = key.IndexOf('|', StringComparison.Ordinal);
        return bar < 0 ? key : Of(key[..bar]) + key[bar..];
    }

    /// <summary>The system of a <c>system|code</c> key.</summary>
    public static string SystemOf(string key)
    {
        int bar = key.IndexOf('|', StringComparison.Ordinal);
        return bar < 0 ? string.Empty : key[..bar];
    }
}
