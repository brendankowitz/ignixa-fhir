/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Subject resources and expression corpora for the Firely-versus-Ignixa differential harness.
 */

using Ignixa.Search.Generated;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// The resources and expressions both engines are driven over.
/// </summary>
/// <remarks>
/// <para>
/// The corpus that decides whether a divergence matters is <see cref="SearchParameterExpressions"/>:
/// those are the expressions <c>TypedElementSearchIndexer</c> runs on every write, so a disagreement
/// there changes what lands in the search index. <see cref="ConstructCorpus"/> covers the language
/// constructs this branch changed, which is where divergence is most likely but least reachable.
/// </para>
/// <para>
/// Resources are held as JSON rather than as pre-built elements so that each engine parses with its
/// own reader. The element model is part of what is being compared - a divergence in how a choice
/// element is typed shows up as an <c>InstanceType</c> difference, and pre-building would hide it.
/// </para>
/// </remarks>
internal static class FirelyParityFixture
{
    /// <summary>
    /// Every distinct non-empty expression shipped in the R4 search parameter definitions, including
    /// the component expressions of composite parameters.
    /// </summary>
    public static IReadOnlyList<string> SearchParameterExpressions { get; } = BuildSearchParameterExpressions();

    /// <summary>
    /// The constructs this branch changed, plus the ones ADR 2608 flags as seam risks.
    /// </summary>
    /// <remarks>
    /// These are driven against the Patient subject. Most are not reachable from any shipped search
    /// parameter, which is the point: the inventory has to say so explicitly rather than leave a
    /// reader to assume every divergence is equally expensive.
    /// </remarks>
    public static IReadOnlyList<string> ConstructCorpus { get; } =
    [
        "birthDate + 1 year",
        "birthDate - 1 month",
        "birthDate + 1 day",
        "birthDate + 30 days",
        "@2012-01-01 + 1 year",
        "@2012-01-31 + 1 month",
        "@2012-02-29 + 1 year",
        "@T10:30:00 + 1 hour",
        "now() > @2000-01-01",
        "today() >= @2000-01-01",
        "birthDate + 1 'a'",
        "birthDate + 1 week",

        "1 'mg' = 1000 'ug'",
        "1 'm' + 1 'cm'",
        "1 'kg' > 1 'g'",
        "2.0 'cm' * 2.0 'm'",
        "1 'mg'",
        "5 'mg' = 5 'mg'",
        "1 year = 1 'a'",

        "'a' in ('a' | 'b')",
        "'z' in ('a' | 'b')",
        "gender in ('male' | 'female')",
        "'a' & 'b'",
        "gender & '!'",
        "{} & 'b'",
        "name.family & '/' & gender",
        "true.not()",
        "false.not()",
        "{}.not()",
        "active.not()",
        "-5",
        "-5 + 3",
        "- multipleBirthInteger",
        "-birthDate",

        "name as HumanName",
        "name.as(HumanName)",
        "deceasedBoolean as boolean",
        "name is HumanName",
        "name.is(HumanName)",
        "gender is code",
        "name.ofType(HumanName)",
        "deceased.ofType(boolean)",
        "identifier.value.ofType(string)",
        "name as string",
        "name.ofType(string)",

        "defineVariable('v', name.family).select(%v)",
        "defineVariable('v', 'x').select(%v & 'y')",
        "defineVariable('v', gender).where(%v = 'male')",
        "%resource.id",
        "%rootResource.id",
        "%context.id",
        "%ucum",
        "%sct",
        "%loinc",
        "%undefinedVariable",

        "@2012 = @2012-01",
        "@2012 > @2012-01",
        "@2012-01-01 = @2012-01-01T00:00:00",
        "birthDate = @1974",
        "birthDate > @1974-12",
        "@T10 = @T10:30",

        "birthDate.lowBoundary()",
        "birthDate.highBoundary()",
        "@2012.lowBoundary()",
        "@2012.highBoundary()",
        "1.5.lowBoundary()",
        "1.587.highBoundary()",
        "birthDate.toString()",

        "name.family.single()",
        "name.single()",
        "iif(active, 'yes', 'no')",
        "iif({}, 'yes', 'no')",
        "name.given.first()",
        "identifier.where(system = 'urn:oid:1.2.3').value",
        "telecom.where(system = 'phone').value",
        "name.exists()",
        "missingElement.exists()",
        "missingElement",
        "children().count()",
        "descendants().count()",
        "repeat(name).count()",
        "extension('http://example.org/ext').value",
        "conformsTo('http://hl7.org/fhir/StructureDefinition/Patient')",
    ];

    /// <summary>
    /// The subject resources. A Patient exercises the bulk of the search parameter corpus, the
    /// Observation carries the components composite parameters read, the Bundle is the only subject
    /// where <c>%resource</c> and <c>%rootResource</c> legitimately differ, and the
    /// StructureDefinition is a deeply nested resource whose search parameters walk long paths.
    /// </summary>
    public static IReadOnlyList<ParityResource> Resources { get; } =
    [
        new ParityResource("Patient", PatientJson),
        new ParityResource("Observation", ObservationJson),
        new ParityResource("Bundle", BundleJson),
        new ParityResource("StructureDefinition", StructureDefinitionJson),
        new ParityResource("QuestionnaireResponse", QuestionnaireResponseJson),
    ];

    /// <summary>
    /// Present for exactly one expression. <c>QuestionnaireResponse-item-subject</c> is the only
    /// shipped R4 search parameter that calls <c>hasExtension()</c>, and against any other resource
    /// its leading path is empty, so the call never runs and the engines' disagreement about it stays
    /// hidden. This resource is what makes that reachable case observable.
    /// </summary>
    private const string QuestionnaireResponseJson = """
    {
      "resourceType": "QuestionnaireResponse",
      "id": "qr1",
      "status": "completed",
      "questionnaire": "http://example.org/Questionnaire/q1",
      "subject": { "reference": "Patient/example" },
      "authored": "2024-06-15T08:00:00Z",
      "item": [
        {
          "linkId": "1",
          "extension": [
            { "url": "http://hl7.org/fhir/StructureDefinition/questionnaireresponse-isSubject", "valueBoolean": true }
          ],
          "answer": [{ "valueReference": { "reference": "Patient/example" } }]
        },
        {
          "linkId": "2",
          "answer": [{ "valueString": "no extension here" }]
        }
      ]
    }
    """;

    private const string PatientJson = """
    {
      "resourceType": "Patient",
      "id": "example",
      "meta": { "lastUpdated": "2024-06-15T08:00:00Z", "profile": ["http://example.org/StructureDefinition/MyPatient"] },
      "active": true,
      "name": [
        { "use": "official", "family": "Smith", "given": ["John", "Q"] },
        { "use": "nickname", "family": "Jones", "given": ["Ann"] }
      ],
      "telecom": [
        { "system": "phone", "value": "555-1234", "use": "home" },
        { "system": "email", "value": "patient@example.org" }
      ],
      "gender": "male",
      "birthDate": "1974-12-25",
      "deceasedBoolean": false,
      "address": [
        { "use": "home", "line": ["1 Main St"], "city": "Springfield", "postalCode": "12345", "country": "US" }
      ],
      "multipleBirthInteger": 2,
      "identifier": [
        {
          "system": "urn:oid:1.2.3",
          "value": "abc",
          "type": { "coding": [{ "system": "http://terminology.hl7.org/CodeSystem/v2-0203", "code": "MR" }] }
        }
      ],
      "managingOrganization": { "reference": "Organization/org1" },
      "generalPractitioner": [{ "reference": "Practitioner/gp1" }],
      "link": [{ "other": { "reference": "Patient/other" }, "type": "seealso" }],
      "communication": [{ "language": { "coding": [{ "system": "urn:ietf:bcp:47", "code": "en" }] } }],
      "extension": [
        { "url": "http://example.org/ext", "valueTime": "10:30:00" }
      ]
    }
    """;

    private const string ObservationJson = """
    {
      "resourceType": "Observation",
      "id": "blood-pressure",
      "status": "final",
      "category": [{ "coding": [{ "system": "http://terminology.hl7.org/CodeSystem/observation-category", "code": "vital-signs" }] }],
      "code": { "coding": [{ "system": "http://loinc.org", "code": "85354-9", "display": "Blood pressure panel" }], "text": "BP panel" },
      "subject": { "reference": "Patient/example" },
      "encounter": { "reference": "Encounter/enc1" },
      "effectiveDateTime": "2024-06-15T08:00:00Z",
      "issued": "2024-06-15T08:05:00Z",
      "performer": [{ "reference": "Practitioner/gp1" }],
      "component": [
        {
          "code": { "coding": [{ "system": "http://loinc.org", "code": "8480-6" }] },
          "valueQuantity": { "value": 120, "unit": "mmHg", "system": "http://unitsofmeasure.org", "code": "mm[Hg]" }
        },
        {
          "code": { "coding": [{ "system": "http://loinc.org", "code": "8462-4" }] },
          "valueQuantity": { "value": 80, "unit": "mmHg", "system": "http://unitsofmeasure.org", "code": "mm[Hg]" }
        },
        {
          "code": { "coding": [{ "system": "http://loinc.org", "code": "6690-2" }] },
          "valueCodeableConcept": { "coding": [{ "system": "http://snomed.info/sct", "code": "271649006" }] }
        }
      ],
      "referenceRange": [{ "low": { "value": 60, "unit": "mmHg" }, "high": { "value": 140, "unit": "mmHg" } }],
      "hasMember": [{ "reference": "Observation/other" }],
      "derivedFrom": [{ "reference": "DocumentReference/doc1" }]
    }
    """;

    private const string BundleJson = """
    {
      "resourceType": "Bundle",
      "id": "bundle-example",
      "type": "searchset",
      "total": 2,
      "timestamp": "2024-06-15T08:00:00Z",
      "link": [{ "relation": "self", "url": "http://example.org/Patient" }],
      "entry": [
        {
          "fullUrl": "http://example.org/Patient/example",
          "resource": {
            "resourceType": "Patient",
            "id": "example",
            "active": true,
            "gender": "female",
            "birthDate": "1980-01-01",
            "name": [{ "family": "Nested", "given": ["Inner"] }]
          },
          "search": { "mode": "match" }
        },
        {
          "fullUrl": "http://example.org/Observation/obs1",
          "resource": {
            "resourceType": "Observation",
            "id": "obs1",
            "status": "final",
            "code": { "coding": [{ "system": "http://loinc.org", "code": "1234-5" }] },
            "subject": { "reference": "Patient/example" },
            "valueQuantity": { "value": 9.5, "unit": "mg", "system": "http://unitsofmeasure.org", "code": "mg" }
          },
          "search": { "mode": "include" }
        }
      ]
    }
    """;

    private const string StructureDefinitionJson = """
    {
      "resourceType": "StructureDefinition",
      "id": "MyPatient",
      "url": "http://example.org/StructureDefinition/MyPatient",
      "version": "1.0.0",
      "name": "MyPatient",
      "title": "My Patient Profile",
      "status": "active",
      "experimental": false,
      "date": "2024-01-01",
      "publisher": "Example Org",
      "contact": [{ "telecom": [{ "system": "url", "value": "http://example.org" }] }],
      "description": "A constrained Patient",
      "jurisdiction": [{ "coding": [{ "system": "urn:iso:std:iso:3166", "code": "US" }] }],
      "kind": "resource",
      "abstract": false,
      "type": "Patient",
      "baseDefinition": "http://hl7.org/fhir/StructureDefinition/Patient",
      "derivation": "constraint",
      "snapshot": {
        "element": [
          { "id": "Patient", "path": "Patient", "min": 0, "max": "*" },
          { "id": "Patient.name", "path": "Patient.name", "min": 1, "max": "*", "type": [{ "code": "HumanName" }] },
          { "id": "Patient.birthDate", "path": "Patient.birthDate", "min": 0, "max": "1", "type": [{ "code": "date" }] }
        ]
      },
      "differential": {
        "element": [{ "id": "Patient.name", "path": "Patient.name", "min": 1 }]
      }
    }
    """;

    private static IReadOnlyList<string> BuildSearchParameterExpressions()
    {
        var expressions = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var parameter in R4SearchParameterDefinitions.GetBaseSearchParameters())
        {
            AddIfPresent(expressions, parameter.Expression);

            foreach (var component in parameter.Component ?? [])
            {
                AddIfPresent(expressions, component.Expression);
            }
        }

        return [.. expressions];
    }

    private static void AddIfPresent(SortedSet<string> target, string? expression)
    {
        if (!string.IsNullOrWhiteSpace(expression))
        {
            target.Add(expression);
        }
    }
}
