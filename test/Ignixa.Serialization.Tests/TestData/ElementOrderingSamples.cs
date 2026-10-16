// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Xunit;

namespace Ignixa.Serialization.Tests.TestData;

/// <summary>
/// Firely-serialized resources used as the reference for spec element order, plus helpers to scramble
/// and compare them.
/// </summary>
public static class ElementOrderingSamples
{
    public static readonly JsonSerializerOptions Relaxed = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static TheoryData<string> ResourceTypes() => new()
    {
        nameof(Patient),
        nameof(Observation),
        nameof(Questionnaire),
        nameof(Bundle),
    };

    /// <summary>Firely's JSON for the sample resource - the expected element order.</summary>
    public static string FirelyJson(string resourceType) => CreateResource(resourceType).ToJson();

    /// <summary>Normalizes formatting and escaping so two documents compare on content and order alone.</summary>
    public static string Canonical(string json) => JsonNode.Parse(json)!.ToJsonString(Relaxed);

    /// <summary>Reverses the properties of every object in the document, at every depth.</summary>
    public static string ReverseAllProperties(string json) => Reverse(JsonNode.Parse(json))!.ToJsonString(Relaxed);

    private static JsonNode? Reverse(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.Reverse().Select(p => KeyValuePair.Create(p.Key, Reverse(p.Value)))),
        JsonArray array => new JsonArray(array.Select(Reverse).ToArray()),
        _ => node?.DeepClone(),
    };

    private static Resource CreateResource(string resourceType) => resourceType switch
    {
        nameof(Patient) => CreatePatient(),
        nameof(Observation) => CreateObservation(),
        nameof(Questionnaire) => CreateQuestionnaire(),
        nameof(Bundle) => new Bundle
        {
            Type = Bundle.BundleType.Transaction,
            Entry =
            [
                new Bundle.EntryComponent
                {
                    FullUrl = "urn:uuid:1",
                    Resource = CreatePatient(),
                    Request = new Bundle.RequestComponent { Method = Bundle.HTTPVerb.POST, Url = "Patient" },
                },
                new Bundle.EntryComponent
                {
                    Resource = CreateObservation(),
                    Request = new Bundle.RequestComponent { Method = Bundle.HTTPVerb.PUT, Url = "Observation/o1" },
                },
            ],
        },
        _ => throw new ArgumentOutOfRangeException(nameof(resourceType), resourceType, "No sample for this resource type."),
    };

    private static Patient CreatePatient()
    {
        var patient = new Patient
        {
            Id = "p1",
            Meta = new Meta { VersionId = "3", Profile = ["http://example.org/StructureDefinition/p"] },
            Text = new Narrative { Status = Narrative.NarrativeStatus.Generated, Div = "<div xmlns=\"http://www.w3.org/1999/xhtml\">Jo &amp; Bo</div>" },
            Contained = [new Organization { Id = "org", Name = "Acme", Active = true }],
            Extension = [new Extension("http://example.org/ext", new FhirString("x"))],
            Identifier = [new Identifier("http://example.org/mrn", "123") { Use = Identifier.IdentifierUse.Official }],
            Active = true,
            Name = [new HumanName { Use = HumanName.NameUse.Official, Family = "Smith", Given = ["Jo", "Q"], Prefix = ["Dr"] }],
            Telecom = [new ContactPoint(ContactPoint.ContactPointSystem.Phone, ContactPoint.ContactPointUse.Home, "555")],
            Gender = AdministrativeGender.Female,
            BirthDate = "1970-01-01",
            Deceased = new FhirBoolean(false),
            Address = [new Address { Line = ["1 Main"], City = "Town", PostalCode = "0000" }],
            Contact =
            [
                new Patient.ContactComponent
                {
                    Relationship = [new CodeableConcept("http://terminology.hl7.org/CodeSystem/v2-0131", "N")],
                    Name = new HumanName { Family = "Smith" },
                    Gender = AdministrativeGender.Male,
                },
            ],
            ManagingOrganization = new ResourceReference("#org"),
        };
        patient.BirthDateElement!.Extension.Add(new Extension("http://example.org/time", new FhirDateTime("1970-01-01T10:00:00Z")));
        return patient;
    }

    private static Observation CreateObservation() => new()
    {
        Id = "o1",
        Status = ObservationStatus.Final,
        Category = [new CodeableConcept("http://terminology.hl7.org/CodeSystem/observation-category", "vital-signs")],
        Code = new CodeableConcept("http://loinc.org", "85354-9", "Blood pressure"),
        Subject = new ResourceReference("Patient/p1"),
        Effective = new FhirDateTime("2024-01-01T10:00:00Z"),
        Value = new Quantity(1.50m, "mg/dL", "http://unitsofmeasure.org"),
        Component =
        [
            new Observation.ComponentComponent
            {
                Code = new CodeableConcept("http://loinc.org", "8480-6"),
                Value = new Quantity(120m, "mm[Hg]", "http://unitsofmeasure.org"),
                Interpretation = [new CodeableConcept("http://terminology.hl7.org/CodeSystem/v3-ObservationInterpretation", "N")],
            },
        ],
    };

    private static Questionnaire CreateQuestionnaire() => new()
    {
        Id = "q1",
        Url = "http://example.org/Questionnaire/q1",
        Status = PublicationStatus.Active,
        Item =
        [
            new Questionnaire.ItemComponent
            {
                LinkId = "1",
                Text = "Group",
                Type = Questionnaire.QuestionnaireItemType.Group,
                Item =
                [
                    new Questionnaire.ItemComponent
                    {
                        LinkId = "1.1",
                        Text = "Smoker?",
                        Type = Questionnaire.QuestionnaireItemType.Boolean,
                        Required = true,
                        EnableWhen = [new Questionnaire.EnableWhenComponent { Question = "1.0", Operator = Questionnaire.QuestionnaireItemOperator.Exists, Answer = new FhirBoolean(true) }],
                        Initial = [new Questionnaire.InitialComponent { Value = new FhirBoolean(false) }],
                    },
                ],
            },
        ],
    };
}
