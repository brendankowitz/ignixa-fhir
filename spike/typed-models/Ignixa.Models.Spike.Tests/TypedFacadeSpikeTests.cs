// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.SourceNodes;
using Shouldly;
using Xunit;
using R4 = Ignixa.Models.R4;
using R5 = Ignixa.Models.R5;

namespace Ignixa.Models.Spike.Tests;

public sealed class TypedFacadeSpikeTests
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

        R4.Patient patient = resource.As<R4.Patient>();

        patient.Name[0].Family.ShouldBe("Chalmers");
        patient.Name[0].Given.ShouldBe(ExpectedGivenNames);
        patient.Gender.ShouldBe(R4.AdministrativeGender.Male);
        patient.BirthDate.ShouldBe("1974-12-25");
        patient.Active.ShouldBe(true);
    }

    [Fact]
    public void GivenR4Patient_WhenSettingGender_ThenWritesThroughToUnderlyingJson()
    {
        var resource = ResourceJsonNode.Parse(PatientJson);
        R4.Patient patient = resource.As<R4.Patient>();

        patient.Gender = R4.AdministrativeGender.Female;

        patient.MutableNode["gender"]!.GetValue<string>().ShouldBe("female");

        var serialized = patient.MutableNode.ToJsonString();
        var reparsed = ResourceJsonNode.Parse(serialized).As<R4.Patient>();
        reparsed.Gender.ShouldBe(R4.AdministrativeGender.Female);
    }

    [Fact]
    public void GivenUnknownElements_WhenMutatingTypedPropertyAndSerializing_ThenUnknownDataSurvives()
    {
        var resource = ResourceJsonNode.Parse(PatientJson);
        R4.Patient patient = resource.As<R4.Patient>();

        patient.Gender = R4.AdministrativeGender.Other;
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
    public void GivenSameNode_WhenViewedAsR4AndR5Patient_ThenBothLensesSeeSameBytes()
    {
        var resource = ResourceJsonNode.Parse(PatientJson);

        R4.Patient p4 = resource.As<R4.Patient>();
        R5.Patient p5 = resource.As<R5.Patient>();

        p4.Gender.ShouldBe(R4.AdministrativeGender.Male);
        p5.Gender.ShouldBe(R5.AdministrativeGender.Male);

        p5.Gender = R5.AdministrativeGender.Female;

        p4.Gender.ShouldBe(R4.AdministrativeGender.Female);
        ReferenceEquals(p4.MutableNode, p5.MutableNode).ShouldBeTrue();
    }

    [Fact]
    public void GivenR5OnlyElement_WhenReadThroughR5Facade_ThenDivergentPropertyIsExposed()
    {
        var resource = ResourceJsonNode.Parse(PatientJson);

        R5.Patient p5 = resource.As<R5.Patient>();

        p5.MaritalStatusText.ShouldBe("Married");

        p5.MaritalStatusText = "Divorced";
        p5.MutableNode["maritalStatus"]!["text"]!.GetValue<string>().ShouldBe("Divorced");
    }
}
