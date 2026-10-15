// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Ignixa.Serialization.Extensions;
using Ignixa.Specification.Generated;
using Shouldly;
using Xunit;

namespace Ignixa.Serialization.Tests;

public class ResourceElementOrdererTests
{
    private static readonly JsonSerializerOptions Relaxed = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly R4CoreSchemaProvider _schema = new();

    public static TheoryData<string> FirelyResources() => new()
    {
        nameof(Patient),
        nameof(Observation),
        nameof(Questionnaire),
        nameof(Bundle),
    };

    [Theory]
    [MemberData(nameof(FirelyResources))]
    public void GivenFirelyResourceWithReversedProperties_WhenOrdered_ThenMatchesFirelyOrder(string resourceType)
    {
        string firelyJson = CreateFirelyResource(resourceType).ToJson();
        byte[] scrambled = Encoding.UTF8.GetBytes(ReverseAllProperties(firelyJson));

        ReadOnlyMemory<byte> ordered = ResourceElementOrderer.Order(scrambled, _schema);

        Canonical(ordered).ShouldBe(Canonical(Encoding.UTF8.GetBytes(firelyJson)));
    }

    [Theory]
    [MemberData(nameof(FirelyResources))]
    public void GivenFirelyOrderedResource_WhenOrdered_ThenReturnsInputUnchanged(string resourceType)
    {
        ReadOnlyMemory<byte> input = Encoding.UTF8.GetBytes(CreateFirelyResource(resourceType).ToJson());

        ReadOnlyMemory<byte> ordered = ResourceElementOrderer.Order(input, _schema);

        ordered.Equals(input).ShouldBeTrue();
    }

    [Fact]
    public void GivenPrimitiveWithExtensionSibling_WhenOrdered_ThenSiblingFollowsPrimitive()
    {
        string json = """{"_birthDate":{"extension":[{"url":"http://e","valueString":"x"}]},"gender":"male","birthDate":"1970-01-01","resourceType":"Patient"}""";

        string ordered = Order(json);

        ordered.ShouldBe("""{"resourceType":"Patient","gender":"male","birthDate":"1970-01-01","_birthDate":{"extension":[{"url":"http://e","valueString":"x"}]}}""");
    }

    [Fact]
    public void GivenUnknownProperties_WhenOrdered_ThenTheyFollowKnownElementsInSourceOrder()
    {
        string json = """{"zeta":1,"id":"a","alpha":2,"resourceType":"Patient"}""";

        string ordered = Order(json);

        ordered.ShouldBe("""{"resourceType":"Patient","id":"a","zeta":1,"alpha":2}""");
    }

    [Fact]
    public void GivenDecimalAndEscapedString_WhenOrdered_ThenLeafBytesAreCopiedVerbatim()
    {
        string json = """{"valueQuantity":{"unit":"mg/dl","value":1.50},"status":"final","resourceType":"Observation"}""";

        string ordered = Order(json);

        ordered.ShouldBe("""{"resourceType":"Observation","status":"final","valueQuantity":{"value":1.50,"unit":"mg/dl"}}""");
    }

    [Fact]
    public void GivenArray_WhenOrdered_ThenElementOrderIsPreserved()
    {
        string json = """{"name":[{"given":["b","a"],"family":"Z"},{"family":"Y"}],"resourceType":"Patient"}""";

        string ordered = Order(json);

        ordered.ShouldBe("""{"resourceType":"Patient","name":[{"family":"Z","given":["b","a"]},{"family":"Y"}]}""");
    }

    [Fact]
    public void GivenNonObjectJson_WhenOrdered_ThenThrowsJsonException()
    {
        Should.Throw<JsonException>(() => ResourceElementOrderer.Order(Encoding.UTF8.GetBytes("[1]"), _schema));
    }

    private string Order(string json) =>
        Encoding.UTF8.GetString(ResourceElementOrderer.Order(Encoding.UTF8.GetBytes(json), _schema).Span);

    private static string Canonical(ReadOnlyMemory<byte> json) =>
        JsonNode.Parse(json.Span)!.ToJsonString(Relaxed);

    private static string ReverseAllProperties(string json) =>
        Reverse(JsonNode.Parse(json))!.ToJsonString(Relaxed);

    private static JsonNode? Reverse(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.Reverse().Select(p => KeyValuePair.Create(p.Key, Reverse(p.Value)))),
        JsonArray array => new JsonArray(array.Select(Reverse).ToArray()),
        _ => node?.DeepClone(),
    };

    private static Resource CreateFirelyResource(string resourceType) => resourceType switch
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
