// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.SourceNodes;
using Shouldly;
using Xunit;

namespace Ignixa.Models.R4.Tests;

/// <summary>
/// Coverage for the additions the graduated generator emits beyond the original spike:
/// typed backbone-element facades (Patient.contact -> PatientContact) and the decimal escape-hatch
/// (Quantity.ValueRaw) that preserves precision beyond <see cref="decimal"/>'s range.
/// </summary>
public sealed class GraduatedAdditionsTests
{
    private const string PatientWithContactJson =
        """
        {
          "resourceType": "Patient",
          "id": "example",
          "contact": [
            {
              "relationship": [ { "text": "Emergency" } ],
              "name": { "family": "Du Marché", "given": [ "Bénédicte" ] },
              "gender": "female",
              "telecom": [ { "system": "phone", "value": "+33 (237) 998327" } ]
            }
          ]
        }
        """;

    [Fact]
    public void GivenPatientContact_WhenReadThroughBackboneFacade_ThenTypedMembersResolve()
    {
        var patient = ResourceJsonNode.Parse(PatientWithContactJson).As<Ignixa.Models.R4.Patient>();

        patient.Contact.Count.ShouldBe(1);
        Ignixa.Models.PatientContact contact = patient.Contact[0];

        contact.Name.ShouldNotBeNull();
        contact.Name!.Family.ShouldBe("Du Marché");
        contact.Name.Given.ShouldBe(["Bénédicte"]);
        contact.Gender.ShouldBe(Ignixa.Models.AdministrativeGender.Female);
        contact.Relationship[0].Text.ShouldBe("Emergency");
        contact.Telecom[0].Value.ShouldBe("+33 (237) 998327");
    }

    [Fact]
    public void GivenNewPatientContact_WhenAddedAndSerialized_ThenWritesThroughBackboneArray()
    {
        var patient = ResourceJsonNode.Parse("""{ "resourceType": "Patient" }""").As<Ignixa.Models.R4.Patient>();

        var contact = new Ignixa.Models.PatientContact
        {
            Gender = Ignixa.Models.AdministrativeGender.Male,
        };
        contact.Name = new Ignixa.Models.HumanName { Family = "Doe" };
        patient.Contact.Add(contact);

        patient.Contact.Count.ShouldBe(1);
        patient.MutableNode["contact"]!.AsArray().Count.ShouldBe(1);
        patient.Contact[0].Name!.Family.ShouldBe("Doe");
        patient.Contact[0].Gender.ShouldBe(Ignixa.Models.AdministrativeGender.Male);

        var reparsed = ResourceJsonNode.Parse(patient.MutableNode.ToJsonString()).As<Ignixa.Models.R4.Patient>();
        reparsed.Contact[0].Gender.ShouldBe(Ignixa.Models.AdministrativeGender.Male);
    }

    [Fact]
    public void GivenHighPrecisionQuantity_WhenReadViaValueRaw_ThenPrecisionBeyondDecimalIsPreserved()
    {
        // 40 significant digits - well beyond System.Decimal's ~28-29 digit capacity.
        const string highPrecisionLiteral = "1.234567890123456789012345678901234567890";

        var obs = ResourceJsonNode.Parse(
            $$"""
            {
              "resourceType": "Observation",
              "status": "final",
              "valueQuantity": { "value": {{highPrecisionLiteral}}, "unit": "x", "code": "x" }
            }
            """).As<Ignixa.Models.R4.Observation>();

        Ignixa.Models.Quantity quantity = obs.ValueQuantity!;

        // The escape-hatch preserves the original lexical value verbatim.
        quantity.ValueRaw!.ToJsonString().ShouldBe(highPrecisionLiteral);

        // decimal? rounds the same value to its ~28-29 significant-digit limit, proving why
        // ValueRaw exists: the typed decimal projection silently drops the trailing digits.
        var roundedDigits = quantity.Value!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        roundedDigits.Length.ShouldBeLessThan(highPrecisionLiteral.Length);
        highPrecisionLiteral.ShouldStartWith(roundedDigits);

        // And ValueRaw survives a serialize round-trip without loss.
        var reparsed = ResourceJsonNode.Parse(obs.MutableNode.ToJsonString()).As<Ignixa.Models.R4.Observation>();
        reparsed.ValueQuantity!.ValueRaw!.ToJsonString().ShouldBe(highPrecisionLiteral);
    }
}
