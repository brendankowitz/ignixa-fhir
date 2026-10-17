// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>
/// Well-known code systems by canonical URI, version independent, with the aliases and OIDs that name them.
/// </summary>
/// <remarks>
/// Sources: the FHIR R4 page "Using Codes in resources: External code systems"
/// (https://hl7.org/fhir/R4/terminologies-systems.html), and the HL7 Terminology (THO) external code system list as
/// published in its NamingSystem resources (hl7.terminology.r4@7.4.0, <c>package/NamingSystem-*.json</c>: the preferred
/// <c>uri</c> uniqueId, the other <c>uri</c>s as aliases, the <c>oid</c>s). <c>http://www.snomed.org/</c> is a common
/// non-preferred spelling of SNOMED CT seen in data, not a THO alias.
/// </remarks>
public static partial class WellKnownSystems
{
    public static IReadOnlyList<WellKnownSystem> All { get; } =
    [
        new("http://snomed.info/sct", "SNOMED CT", "SNOMED International", ["http://www.snomed.org/", "http://www.snomed.org/sct"], ["2.16.840.1.113883.6.96"]),
        new("http://loinc.org", "LOINC", "Regenstrief Institute", [], ["2.16.840.1.113883.6.1"]),
        new("http://www.nlm.nih.gov/research/umls/rxnorm", "RxNorm", "U.S. National Library of Medicine", [], ["2.16.840.1.113883.6.88"]),
        new("http://hl7.org/fhir/sid/icd-10-cm", "ICD-10-CM", "U.S. National Center for Health Statistics", ["http://terminology.hl7.org/CodeSystem/icd10CM"], ["2.16.840.1.113883.6.90"]),
        new("http://hl7.org/fhir/sid/icd-10", "ICD-10", "World Health Organization", ["http://terminology.hl7.org/CodeSystem/icd10"], ["2.16.840.1.113883.6.3"]),
        new("http://www.cms.gov/Medicare/Coding/ICD10", "ICD-10-PCS", "U.S. Centers for Medicare & Medicaid Services", ["http://terminology.hl7.org/CodeSystem/icd10PCS"], ["2.16.840.1.113883.6.4"]),
        new("http://hl7.org/fhir/sid/icd-9-cm", "ICD-9-CM", "U.S. National Center for Health Statistics", ["http://terminology.hl7.org/CodeSystem/icd9cm"], ["2.16.840.1.113883.6.103", "2.16.840.1.113883.6.104", "2.16.840.1.113883.6.2"]),
        new("http://id.who.int/icd/release/11/mms", "ICD-11 MMS", "World Health Organization", [], []),
        new("http://www.ama-assn.org/go/cpt", "CPT", "American Medical Association", [], ["2.16.840.1.113883.6.12"]),
        new("http://www.cms.gov/Medicare/Coding/HCPCSReleaseCodeSets", "HCPCS Level II", "U.S. Centers for Medicare & Medicaid Services", ["https://www.cms.gov/Medicare/Coding/HCPCSReleaseCodeSets"], ["2.16.840.1.113883.6.285"]),
        new("http://hl7.org/fhir/sid/ndc", "NDC", "U.S. Food and Drug Administration", [], ["2.16.840.1.113883.6.69"]),
        new("http://hl7.org/fhir/sid/cvx", "CVX", "U.S. Centers for Disease Control and Prevention", ["http://terminology.hl7.org/CodeSystem/CVX"], ["2.16.840.1.113883.12.292"]),
        new("http://unitsofmeasure.org", "UCUM", "Regenstrief Institute", [], ["2.16.840.1.113883.6.8"]),
        new("http://nucc.org/provider-taxonomy", "NUCC Health Care Provider Taxonomy", "National Uniform Claim Committee", ["http://terminology.hl7.org/CodeSystem/v3-HealthcareProviderTaxonomyHIPAA", "http://terminology.hl7.org/CodeSystem/v3-nuccProviderCodes"], ["2.16.840.1.113883.6.101"]),
        new("http://hl7.org/fhir/ndfrt", "NDF-RT", "U.S. Department of Veterans Affairs", [], ["2.16.840.1.113883.6.209"]),
        new("http://va.gov/terminology/medrt", "MED-RT", "U.S. Department of Veterans Affairs", [], ["2.16.840.1.113883.6.345"]),
        new("http://dicom.nema.org/resources/ontology/DCM", "DICOM Controlled Terminology", "National Electrical Manufacturers Association (DICOM)", ["http://dicom.nema.org/medical/dicom"], ["1.2.840.10008.2.16.4"]),
    ];

    private static readonly Dictionary<string, (WellKnownSystem System, bool Preferred)> ByUri = Build();

    /// <summary>What <paramref name="uri"/> names: a well-known system (preferred or not), an HL7 Terminology or FHIR family, or nothing known.</summary>
    public static SystemIdentity Identify(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (ByUri.TryGetValue(uri, out (WellKnownSystem System, bool Preferred) known))
        {
            return new SystemIdentity("external", known.System.Name, known.System.Steward, known.Preferred ? null : known.System.PreferredUri);
        }

        if (ThoMember().Match(uri) is { Success: true } tho)
        {
            string name = tho.Groups["name"].Value;
            return name.StartsWith("v2-", StringComparison.Ordinal)
                ? new SystemIdentity("hl7-v2", $"HL7 v2 table {name[3..]}", "HL7 International", null)
                : name.StartsWith("v3-", StringComparison.Ordinal)
                    ? new SystemIdentity("hl7-v3", $"HL7 v3 {name[3..]}", "HL7 International", null)
                    : new SystemIdentity("hl7-terminology", $"HL7 Terminology {name}", "HL7 International", null);
        }

        return FhirMember().Match(uri) is { Success: true } fhir
            ? new SystemIdentity("fhir", $"FHIR {fhir.Groups["name"].Value}", "HL7 International", null)
            : new SystemIdentity("unknown", null, null, null);
    }

    private static Dictionary<string, (WellKnownSystem, bool)> Build()
    {
        var map = new Dictionary<string, (WellKnownSystem, bool)>(StringComparer.Ordinal);
        foreach (WellKnownSystem system in All)
        {
            map[system.PreferredUri] = (system, true);
            foreach (string alias in system.Aliases)
            {
                map[alias] = (system, false);
            }

            foreach (string oid in system.Oids)
            {
                map["urn:oid:" + oid] = (system, false);
            }
        }

        return map;
    }

    [GeneratedRegex("^http://terminology\\.hl7\\.org/CodeSystem/(?<name>[A-Za-z0-9.\\-_]+)$")]
    private static partial Regex ThoMember();

    [GeneratedRegex("^http://hl7\\.org/fhir/(?<name>[A-Za-z0-9.\\-_/]+)$")]
    private static partial Regex FhirMember();
}
