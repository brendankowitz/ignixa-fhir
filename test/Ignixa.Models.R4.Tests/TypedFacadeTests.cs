// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Serialization.TestSupport;
using Shouldly;
using Xunit;

namespace Ignixa.Models.R4.Tests;

/// <summary>
/// Core facade behaviours: typed read-through, write-through, unknown-element survival, and viewing
/// the same node through more than one lens. Ported from the typed-models spike (TypedFacadeSpikeTests).
/// </summary>
public sealed class TypedFacadeTests
{
    private static readonly string[] ExpectedGivenNames = ["Peter", "James"];

    private const string PatientJson =
        """
        {
          "resourceType": "Patient",
          "id": "example",
          "active": true,
          "name": [
            {
              "family": "Chalmers",
              "given": [ "Peter", "James" ]
            }
          ],
          "gender": "male",
          "birthDate": "1974-12-25",
          "maritalStatus": { "text": "Married" },
          "extension": [
            {
              "url": "http://example.org/spike/unknown-element",
              "valueString": "must-survive-round-trip"
            }
          ],
          "_birthDate": {
            "extension": [
              {
                "url": "http://hl7.org/fhir/StructureDefinition/patient-birthTime",
                "valueDateTime": "1974-12-25T14:35:45-05:00"
              }
            ]
          }
        }
        """;

    [Fact]
    public void GivenPatientJson_WhenViewedAsR4Patient_ThenTypedPropertiesReadThrough()
    {
        var resource = ResourceJsonNode.Parse(PatientJson);

        Ignixa.Models.R4.Patient patient = resource.As<Ignixa.Models.R4.Patient>();

        patient.Name[0].Family.ShouldBe("Chalmers");
        patient.Name[0].Given.ShouldBe(ExpectedGivenNames);
        patient.Gender.ShouldBe(Ignixa.Models.AdministrativeGender.Male);
        patient.BirthDate.ShouldBe("1974-12-25");
        patient.Active.ShouldBe(true);
    }

    [Fact]
    public void GivenR4Patient_WhenSettingGender_ThenWritesThroughToUnderlyingJson()
    {
        var resource = ResourceJsonNode.Parse(PatientJson);
        Ignixa.Models.R4.Patient patient = resource.As<Ignixa.Models.R4.Patient>();

        patient.Gender = Ignixa.Models.AdministrativeGender.Female;

        patient.MutableNode()["gender"]!.GetValue<string>().ShouldBe("female");

        var serialized = patient.SerializeToString();
        var reparsed = ResourceJsonNode.Parse(serialized).As<Ignixa.Models.R4.Patient>();
        reparsed.Gender.ShouldBe(Ignixa.Models.AdministrativeGender.Female);
    }

    [Fact]
    public void GivenUnknownElements_WhenMutatingTypedPropertyAndSerializing_ThenUnknownDataSurvives()
    {
        var resource = ResourceJsonNode.Parse(PatientJson);
        Ignixa.Models.R4.Patient patient = resource.As<Ignixa.Models.R4.Patient>();

        patient.Gender = Ignixa.Models.AdministrativeGender.Other;
        patient.BirthDate = "1975-01-01";

        var serialized = patient.SerializeToString();

        serialized.ShouldContain("must-survive-round-trip");
        serialized.ShouldContain("patient-birthTime");
        serialized.ShouldContain("_birthDate");

        var reparsed = ResourceJsonNode.Parse(serialized);
        reparsed.MutableNode()["extension"]!.AsArray().Count.ShouldBe(1);
        reparsed.MutableNode()["_birthDate"].ShouldNotBeNull();
    }

    [Fact]
    public void GivenSameNode_WhenViewedThroughTwoFacadesOverSameNode_ThenBothLensesSeeSameBytes()
    {
        var resource = ResourceJsonNode.Parse(PatientJson);

        Ignixa.Models.R4.Patient a = resource.As<Ignixa.Models.R4.Patient>();
        Ignixa.Models.R4.Patient b = resource.As<Ignixa.Models.R4.Patient>();

        a.Gender.ShouldBe(Ignixa.Models.AdministrativeGender.Male);
        b.Gender.ShouldBe(Ignixa.Models.AdministrativeGender.Male);

        b.Gender = Ignixa.Models.AdministrativeGender.Female;

        a.Gender.ShouldBe(Ignixa.Models.AdministrativeGender.Female);
        ReferenceEquals(a.MutableNode(), b.MutableNode()).ShouldBeTrue();
    }

    [Fact]
    public void GivenComplexValueAlreadyAttached_WhenAssignedToAnotherParent_ThenItIsClonedNotThrown()
    {
        var cc = new Ignixa.Models.CodeableConcept(
            new System.Text.Json.Nodes.JsonObject { ["text"] = "Married" });

        var p1 = ResourceJsonNode.Parse("""{"resourceType":"Patient"}""").As<Ignixa.Models.R4.Patient>();
        var p2 = ResourceJsonNode.Parse("""{"resourceType":"Patient"}""").As<Ignixa.Models.R4.Patient>();

        p1.MaritalStatus = cc; // attaches cc.MutableNode under p1

        // cc.MutableNode now has a parent; a naive assignment would throw "node already has a parent".
        Should.NotThrow(() => p2.MaritalStatus = cc);

        p1.MaritalStatus!.MutableNode()["text"]!.GetValue<string>().ShouldBe("Married");
        p2.MaritalStatus!.MutableNode()["text"]!.GetValue<string>().ShouldBe("Married");
        ReferenceEquals(p1.MaritalStatus!.MutableNode(), p2.MaritalStatus!.MutableNode()).ShouldBeFalse();
    }

    // -- Reference-typed elements (typed Reference facade as of the Plan A generator fix) -----------

    [Fact]
    public void GivenReferenceTypedElement_WhenSetAndSerialized_ThenRoundTripsThroughReparse()
    {
        // Observation.subject is one of the most heavily-used Reference-typed elements in FHIR.
        // Reference2 is the `reference` field's accessor -- named Reference2 because a property cannot
        // share its enclosing type's name (the same collision guard that produces Extension.Extension2).
        var obs = ResourceJsonNode.Parse("""{ "resourceType": "Observation", "status": "final" }""")
            .As<Ignixa.Models.R4.Observation>();

        obs.Subject = new Ignixa.Models.Reference
        {
            Reference2 = "Patient/123",
            Display = "Jean Chalmers",
        };

        obs.Subject!.Reference2.ShouldBe("Patient/123");

        var reparsed = ResourceJsonNode.Parse(obs.SerializeToString()).As<Ignixa.Models.R4.Observation>();
        reparsed.Subject!.Reference2.ShouldBe("Patient/123");
        reparsed.Subject!.Display.ShouldBe("Jean Chalmers");
    }

    [Fact]
    public void GivenReferenceValueAlreadyAttached_WhenAssignedToAnotherParent_ThenItIsClonedNotThrown()
    {
        // The typed-complex setter (EmitComplexProperty) routes through the same BaseJsonNode.SetProperty
        // as every other complex property (see GivenComplexValueAlreadyAttached... above), so the same
        // clone-on-reparent guarantee should hold here too -- worth pinning directly rather than assuming.
        var reference = new Ignixa.Models.Reference { Reference2 = "Patient/123" };

        var obs1 = ResourceJsonNode.Parse("""{ "resourceType": "Observation", "status": "final" }""").As<Ignixa.Models.R4.Observation>();
        var obs2 = ResourceJsonNode.Parse("""{ "resourceType": "Observation", "status": "final" }""").As<Ignixa.Models.R4.Observation>();

        obs1.Subject = reference; // attaches `reference`'s underlying node under obs1

        Should.NotThrow(() => obs2.Subject = reference);

        obs1.Subject!.Reference2.ShouldBe("Patient/123");
        obs2.Subject!.Reference2.ShouldBe("Patient/123");
        ReferenceEquals(obs1.Subject!.MutableNode(), obs2.Subject!.MutableNode()).ShouldBeFalse();
    }

    [Fact]
    public void GivenExtensionWithValueString_WhenValueReferenceIsSetInstead_ThenValueStringIsCleared()
    {
        // Locks in a real bug the Plan A generator fix resolved: Extension.value[x]'s Reference variant
        // was previously dropped entirely (RecordDroppedChoiceVariant), so it was never added to
        // ValueVariantKeys -- meaning Set{Base}Variant, which only clears keys present in that array,
        // could never clear a stale valueReference when a different variant was set (or vice versa,
        // as pinned here). Now that Reference is a real variant, it participates in the same clearing
        // loop as every other variant.
        var ext = new Ignixa.Models.R4.Extension { Url = "http://example.org/ext" };

        ext.ValueString = "hello";
        ext.ValueType.ShouldBe(Ignixa.Models.R4.ExtensionValueType.String);

        ext.ValueReference = new Ignixa.Models.Reference { Reference2 = "Patient/123" };

        ext.ValueType.ShouldBe(Ignixa.Models.R4.ExtensionValueType.Reference);
        ext.ValueString.ShouldBeNull();
        ext.ValueReference!.Reference2.ShouldBe("Patient/123");
    }
}
