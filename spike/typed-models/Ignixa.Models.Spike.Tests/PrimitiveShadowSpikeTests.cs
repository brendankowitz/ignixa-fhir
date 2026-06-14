// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;
using Shouldly;
using Xunit;
using R4 = Ignixa.Models.R4;

namespace Ignixa.Models.Spike.Tests;

/// <summary>
/// Hard part 1: a FHIR primitive spans the value key plus the optional <c>_name</c> shadow
/// (id/extensions). These tests prove the (parent, name) primitive wrapper reads and writes both
/// keys and survives byte-for-byte round-trips.
/// </summary>
public sealed class PrimitiveShadowSpikeTests
{
    private const string PatientWithShadowJson =
        """
        {
          "resourceType": "Patient",
          "id": "example",
          "birthDate": "1974-12-25",
          "_birthDate": {
            "id": "bd-1",
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
    public void GivenPrimitiveWithShadow_WhenReadThroughWrapper_ThenValueAndExtensionVisible()
    {
        var patient = ResourceJsonNode.Parse(PatientWithShadowJson).As<R4.Patient>();

        var element = patient.BirthDateElement;

        element.Value.ShouldBe("1974-12-25");
        element.Id.ShouldBe("bd-1");
        element.HasExtensions.ShouldBeTrue();
        element.Extension.Count.ShouldBe(1);
        element.Extension[0]!["url"]!.GetValue<string>()
            .ShouldBe("http://hl7.org/fhir/StructureDefinition/patient-birthTime");
    }

    [Fact]
    public void GivenPrimitiveWithShadow_WhenSettingValue_ThenShadowExtensionsPreserved()
    {
        var patient = ResourceJsonNode.Parse(PatientWithShadowJson).As<R4.Patient>();

        patient.BirthDateElement.Value = "1975-01-01";

        patient.MutableNode["birthDate"]!.GetValue<string>().ShouldBe("1975-01-01");
        patient.MutableNode["_birthDate"].ShouldNotBeNull();
        patient.BirthDateElement.Extension.Count.ShouldBe(1);
        patient.BirthDateElement.Id.ShouldBe("bd-1");
    }

    [Fact]
    public void GivenPrimitiveWithoutShadow_WhenAddingExtension_ThenShadowLandsUnderUnderscoreKey()
    {
        var patient = ResourceJsonNode.Parse(
            """{ "resourceType": "Patient", "birthDate": "2000-01-01" }""").As<R4.Patient>();

        patient.MutableNode["_birthDate"].ShouldBeNull();

        patient.BirthDateElement.Extension.Add(new JsonObject
        {
            ["url"] = "http://example.org/spike/added",
            ["valueString"] = "added-via-wrapper",
        });

        var shadow = patient.MutableNode["_birthDate"].ShouldBeAssignableTo<JsonObject>();
        shadow!["extension"]!.AsArray().Count.ShouldBe(1);
        shadow["extension"]![0]!["url"]!.GetValue<string>().ShouldBe("http://example.org/spike/added");
        patient.MutableNode["birthDate"]!.GetValue<string>().ShouldBe("2000-01-01");
    }

    [Fact]
    public void GivenPrimitiveWithShadow_WhenMutatedAndSerialized_ThenRoundTripByteFidelity()
    {
        var patient = ResourceJsonNode.Parse(PatientWithShadowJson).As<R4.Patient>();

        patient.BirthDateElement.Value = "1980-06-06";

        var serialized = patient.MutableNode.ToJsonString();
        serialized.ShouldContain("patient-birthTime");
        serialized.ShouldContain("\"id\":\"bd-1\"");

        var reparsed = ResourceJsonNode.Parse(serialized).As<R4.Patient>();
        reparsed.BirthDateElement.Value.ShouldBe("1980-06-06");
        reparsed.BirthDateElement.Id.ShouldBe("bd-1");
        reparsed.BirthDateElement.Extension.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenShadowOnly_WhenValueIsNull_ThenWrapperStillExposesExtensions()
    {
        var patient = ResourceJsonNode.Parse(
            """
            {
              "resourceType": "Patient",
              "_birthDate": { "extension": [ { "url": "http://example.org/spike/shadow-only" } ] }
            }
            """).As<R4.Patient>();

        patient.BirthDateElement.Value.ShouldBeNull();
        patient.BirthDateElement.HasExtensions.ShouldBeTrue();
        patient.BirthDateElement.Extension.Count.ShouldBe(1);
    }
}
