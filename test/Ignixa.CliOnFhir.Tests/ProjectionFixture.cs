// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Search;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>A Synthea-shaped folder and helpers shared by the <c>columns</c> and <c>fhirpath</c> tests.</summary>
internal static class ProjectionFixture
{
    public const string PatientFile = "Ann_Smith_pa.json";

    public const string PractitionersFile = "practitionerInformation.json";

    /// <summary>
    /// Synthea's shape: a batch Bundle of practitioners and organizations, and a patient's transaction Bundle that references
    /// them conditionally (<c>Type?identifier=system|value</c>) and its own entries by <c>urn:uuid</c>. The patient has three
    /// medication requests with a medicationCodeableConcept (one by an NPI no practitioner has) and one with a medicationReference, two encounters (one emergency), a metabolic panel whose
    /// results are referenced by urn:uuid, and a condition coded twice.
    /// </summary>
    public static TestFolder Synthea() => new TestFolder()
        .Write(PractitionersFile, """
            {"resourceType":"Bundle","type":"batch","entry":[
              {"fullUrl":"urn:uuid:pr-1","resource":{"resourceType":"Practitioner","id":"pr1","identifier":[{"system":"http://hl7.org/fhir/sid/us-npi","value":"111"}],"name":[{"family":"Marquardt","given":["Bettina"]}]},"request":{"method":"POST","url":"Practitioner"}},
              {"fullUrl":"urn:uuid:pr-2","resource":{"resourceType":"Practitioner","id":"pr2","identifier":[{"system":"http://hl7.org/fhir/sid/us-npi","value":"222"}],"name":[{"family":"Okafor","given":["Ada"]}]},"request":{"method":"POST","url":"Practitioner"}},
              {"fullUrl":"urn:uuid:org-1","resource":{"resourceType":"Organization","id":"org1","identifier":[{"system":"https://github.com/synthetichealth/synthea","value":"org-1"}],"name":"General Hospital"},"request":{"method":"POST","url":"Organization"}}
            ]}
            """)
        .Write(PatientFile, """
            {"resourceType":"Bundle","type":"transaction","entry":[
              {"fullUrl":"urn:uuid:pa","resource":{"resourceType":"Patient","id":"pa","name":[{"family":"Smith","given":["Ann"]}],"birthDate":"1950-04-02"},"request":{"method":"POST","url":"Patient"}},
              {"fullUrl":"urn:uuid:e-1","resource":{"resourceType":"Encounter","id":"e1","status":"finished","class":{"system":"http://terminology.hl7.org/CodeSystem/v3-ActCode","code":"AMB"},"subject":{"reference":"urn:uuid:pa"},"period":{"start":"2020-01-05T10:00:00Z"},
                "serviceProvider":{"reference":"Organization?identifier=https://github.com/synthetichealth/synthea|org-1"}},"request":{"method":"POST","url":"Encounter"}},
              {"fullUrl":"urn:uuid:e-2","resource":{"resourceType":"Encounter","id":"e2","status":"finished","class":{"system":"http://terminology.hl7.org/CodeSystem/v3-ActCode","code":"EMER"},"subject":{"reference":"urn:uuid:pa"},"period":{"start":"2021-03-09T22:00:00Z"},
                "serviceProvider":{"reference":"Organization?identifier=https://github.com/synthetichealth/synthea|org-1"}},"request":{"method":"POST","url":"Encounter"}},
              {"fullUrl":"urn:uuid:m-1","resource":{"resourceType":"MedicationRequest","id":"m1","status":"stopped","intent":"order","authoredOn":"2019-02-01",
                "medicationCodeableConcept":{"coding":[{"system":"http://www.nlm.nih.gov/research/umls/rxnorm","code":"197361","display":"Amlodipine 5 MG Oral Tablet"}]},
                "subject":{"reference":"urn:uuid:pa"},"encounter":{"reference":"urn:uuid:e-1"},"requester":{"reference":"Practitioner?identifier=http://hl7.org/fhir/sid/us-npi|111"}},"request":{"method":"POST","url":"MedicationRequest"}},
              {"fullUrl":"urn:uuid:m-2","resource":{"resourceType":"MedicationRequest","id":"m2","status":"active","intent":"order","authoredOn":"2021-03-10",
                "medicationCodeableConcept":{"coding":[{"system":"http://www.nlm.nih.gov/research/umls/rxnorm","code":"314076","display":"lisinopril 10 MG Oral Tablet"}]},
                "subject":{"reference":"urn:uuid:pa"},"encounter":{"reference":"urn:uuid:e-2"},"requester":{"reference":"Practitioner?identifier=http://hl7.org/fhir/sid/us-npi|222"}},"request":{"method":"POST","url":"MedicationRequest"}},
              {"fullUrl":"urn:uuid:m-3","resource":{"resourceType":"MedicationRequest","id":"m3","status":"active","intent":"order","authoredOn":"2022-07-15",
                "medicationCodeableConcept":{"coding":[{"system":"http://www.nlm.nih.gov/research/umls/rxnorm","code":"860975","display":"metformin 500 MG Oral Tablet"}]},
                "subject":{"reference":"urn:uuid:pa"},"requester":{"reference":"Practitioner?identifier=http://hl7.org/fhir/sid/us-npi|999"}},"request":{"method":"POST","url":"MedicationRequest"}},
              {"fullUrl":"urn:uuid:med-1","resource":{"resourceType":"Medication","id":"med1","code":{"coding":[{"system":"http://www.nlm.nih.gov/research/umls/rxnorm","code":"200031","display":"carvedilol 6.25 MG Oral Tablet"}]}},"request":{"method":"POST","url":"Medication"}},
              {"fullUrl":"urn:uuid:m-4","resource":{"resourceType":"MedicationRequest","id":"m4","status":"active","intent":"order","authoredOn":"2023-01-20",
                "medicationReference":{"reference":"urn:uuid:med-1"},
                "subject":{"reference":"urn:uuid:pa"},"requester":{"reference":"Practitioner?identifier=http://hl7.org/fhir/sid/us-npi|111"}},"request":{"method":"POST","url":"MedicationRequest"}},
              {"fullUrl":"urn:uuid:o-k","resource":{"resourceType":"Observation","id":"ok","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"6298-4","display":"Potassium"}]},"subject":{"reference":"urn:uuid:pa"},"effectiveDateTime":"2021-03-09T23:00:00Z","valueQuantity":{"value":4.1,"unit":"mmol/L"}},"request":{"method":"POST","url":"Observation"}},
              {"fullUrl":"urn:uuid:o-na","resource":{"resourceType":"Observation","id":"ona","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"2947-0","display":"Sodium"}]},"subject":{"reference":"urn:uuid:pa"},"effectiveDateTime":"2021-03-09T23:00:00Z","valueQuantity":{"value":139,"unit":"mmol/L"}},"request":{"method":"POST","url":"Observation"}},
              {"fullUrl":"urn:uuid:o-a1","resource":{"resourceType":"Observation","id":"a1","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"4548-4"}]},"subject":{"reference":"urn:uuid:pa"},"effectiveDateTime":"2019-06-01","valueQuantity":{"value":6.2,"unit":"%"}},"request":{"method":"POST","url":"Observation"}},
              {"fullUrl":"urn:uuid:o-a2","resource":{"resourceType":"Observation","id":"a2","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"4548-4"}]},"subject":{"reference":"urn:uuid:pa"},"effectiveDateTime":"2022-06-01","valueQuantity":{"value":7.4,"unit":"%"}},"request":{"method":"POST","url":"Observation"}},
              {"fullUrl":"urn:uuid:o-a3","resource":{"resourceType":"Observation","id":"a3","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"4548-4"}]},"subject":{"reference":"urn:uuid:pa"},"effectiveDateTime":"2020-06-01","valueQuantity":{"value":6.8,"unit":"%"}},"request":{"method":"POST","url":"Observation"}},
              {"fullUrl":"urn:uuid:dr-1","resource":{"resourceType":"DiagnosticReport","id":"dr1","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"51990-0","display":"Basic metabolic panel"}]},"subject":{"reference":"urn:uuid:pa"},"encounter":{"reference":"urn:uuid:e-2"},"effectiveDateTime":"2021-03-09T23:00:00Z",
                "result":[{"reference":"urn:uuid:o-na"},{"reference":"urn:uuid:o-k"}]},"request":{"method":"POST","url":"DiagnosticReport"}},
              {"fullUrl":"urn:uuid:c-1","resource":{"resourceType":"Condition","id":"c1","clinicalStatus":{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/condition-clinical","code":"active"}]},
                "code":{"coding":[{"system":"http://snomed.info/sct","code":"49436004","display":"Atrial fibrillation"},{"system":"http://hl7.org/fhir/sid/icd-10-cm","code":"I48.91"}]},"subject":{"reference":"urn:uuid:pa"}},"request":{"method":"POST","url":"Condition"}}
            ]}
            """);

    /// <summary>The prescriber column of the README and the server instructions, plus the medication's date and display.</summary>
    public static IReadOnlyList<SearchColumn> MedicationColumns { get; } =
    [
        new("date", "authoredOn"),
        new("drug", "(medication.ofType(CodeableConcept) | medication.ofType(Reference).resolve().code).coding.first().display"),
        new("prescriber", "requester.resolve().name.first().family"),
    ];

    public static SearchFacts Facts(ToolEnvelope<SearchFacts?> envelope) => envelope.Facts.ShouldNotBeNull();

    /// <summary>The table's rows as compact JSON, for comparing whole rows.</summary>
    public static string[] Rows(ToolEnvelope<SearchFacts?> envelope) =>
        [.. Facts(envelope).Rows.ShouldNotBeNull().Select(row => row.ToJsonString())];

    /// <summary>The references evidence cites, in order.</summary>
    public static string[] Cited(ToolEnvelope<SearchFacts?> envelope) => [.. envelope.Evidence.Select(item => item.Reference)];

    public static IssueItem Error(ToolEnvelope<SearchFacts?> envelope)
    {
        envelope.Facts.ShouldBeNull();
        return envelope.Issues.Where(issue => issue.Severity == "error").ShouldHaveSingleItem();
    }

    public static JsonNode? Cell(ToolEnvelope<SearchFacts?> envelope, int row, string column)
    {
        SearchFacts facts = Facts(envelope);
        int index = facts.Columns.ShouldNotBeNull().ToList().IndexOf(column);
        index.ShouldBeGreaterThan(0);
        return facts.Rows.ShouldNotBeNull()[row][index];
    }
}
