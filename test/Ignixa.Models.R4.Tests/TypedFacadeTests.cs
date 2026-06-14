// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.SourceNodes;
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

        patient.MutableNode["gender"]!.GetValue<string>().ShouldBe("female");

        var serialized = patient.MutableNode.ToJsonString();
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

        var serialized = patient.MutableNode.ToJsonString();

        serialized.ShouldContain("must-survive-round-trip");
        serialized.ShouldContain("patient-birthTime");
        serialized.ShouldContain("_birthDate");

        var reparsed = ResourceJsonNode.Parse(serialized);
        reparsed.MutableNode["extension"]!.AsArray().Count.ShouldBe(1);
        reparsed.MutableNode["_birthDate"].ShouldNotBeNull();
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
        ReferenceEquals(a.MutableNode, b.MutableNode).ShouldBeTrue();
    }
}
