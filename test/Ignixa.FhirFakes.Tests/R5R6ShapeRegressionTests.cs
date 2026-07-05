// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Shouldly;
using Ignixa.Abstractions;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Xunit.Abstractions;

namespace Ignixa.FhirFakes.Tests;

/// <summary>
/// Guards the R5/R6 shape changes for Encounter, Organization, Immunization, and Procedure that
/// were previously emitted with the R4 shape and rejected by the real R5/R6 schemas. Each test
/// asserts the concrete field name/type per version so a coincidentally-valid-but-wrong shape can't
/// slip through unnoticed.
/// </summary>
public class R5R6ShapeRegressionTests
{
    private readonly ITestOutputHelper _output;
    private readonly List<IFhirSchemaProvider> _schemaProviders;

    public R5R6ShapeRegressionTests(ITestOutputHelper output)
    {
        _output = output;
        _schemaProviders =
        [
            new STU3CoreSchemaProvider(),
            new R4CoreSchemaProvider(),
            new R4BCoreSchemaProvider(),
            new R5CoreSchemaProvider(),
            new R6CoreSchemaProvider()
        ];
    }

    [Fact]
    public void GivenEncounterWithPractitioner_WhenGeneratedAcrossAllVersions_ThenUsesVersionCorrectActorField()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing Encounter.participant with {schema.Version}");

            var scenario = new ScenarioBuilder(schema)
                .WithPatient()
                .AddFamilyPractitioner()
                .AddEncounter("Office visit")
                .Build();

            var participant = scenario.Encounters[0].MutableNode["participant"]?[0];
            participant.ShouldNotBeNull($"participant should exist in {schema.Version}");

            // R5 renamed Encounter.participant.individual to Encounter.participant.actor.
            if (schema.Version >= FhirVersion.R5)
            {
                participant!["actor"].ShouldNotBeNull($"R5+ uses participant.actor in {schema.Version}");
                participant["individual"].ShouldBeNull($"R5+ dropped participant.individual in {schema.Version}");
            }
            else
            {
                participant!["individual"].ShouldNotBeNull($"pre-R5 uses participant.individual in {schema.Version}");
                participant["actor"].ShouldBeNull($"pre-R5 has no participant.actor in {schema.Version}");
            }
        }
    }

    [Fact]
    public void GivenEncounterWithFinishedStatus_WhenGeneratedAcrossAllVersions_ThenMapsToVersionValidCode()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing Encounter.status mapping with {schema.Version}");

            var scenario = new ScenarioBuilder(schema)
                .WithPatient()
                .AddState(new EncounterState
                {
                    Name = "Finished_Encounter",
                    Status = "finished"
                })
                .Build();

            var status = scenario.Encounters[0].MutableNode["status"]?.GetValue<string>();

            // R5 removed "finished" from the encounter-status value set, renaming it to "completed".
            var expected = schema.Version >= FhirVersion.R5 ? "completed" : "finished";
            status.ShouldBe(expected, $"status should be '{expected}' in {schema.Version}");
        }
    }

    [Fact]
    public void GivenOrganization_WhenGeneratedAcrossAllVersions_ThenPlacesTelecomAndAddressPerVersion()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing Organization telecom/address placement with {schema.Version}");

            var scenario = new ScenarioBuilder(schema)
                .WithPatient()
                .AddHospital("Boston Medical Center")
                .Build();

            var org = scenario.Organizations[0].MutableNode;

            // R5 removed Organization.telecom and Organization.address, folding both into
            // Organization.contact (ExtendedContactDetail).
            if (schema.Version >= FhirVersion.R5)
            {
                org["telecom"].ShouldBeNull($"R5+ dropped Organization.telecom in {schema.Version}");
                org["address"].ShouldBeNull($"R5+ dropped Organization.address in {schema.Version}");

                var contact = org["contact"] as JsonArray;
                contact.ShouldNotBeNull($"R5+ should use Organization.contact in {schema.Version}");
                contact!.Count.ShouldBeGreaterThan(0, $"contact should have an entry in {schema.Version}");
                (contact[0]?["telecom"] as JsonArray).ShouldNotBeNull($"contact.telecom should exist in {schema.Version}");
                contact[0]?["address"].ShouldNotBeNull($"contact.address should exist in {schema.Version}");
                // ExtendedContactDetail.address is 0..1 (a single object, not an array).
                (contact[0]?["address"] is JsonArray).ShouldBeFalse($"contact.address must be a single object in {schema.Version}");
            }
            else
            {
                (org["telecom"] as JsonArray).ShouldNotBeNull($"pre-R5 uses top-level Organization.telecom in {schema.Version}");
                (org["address"] as JsonArray).ShouldNotBeNull($"pre-R5 uses top-level Organization.address in {schema.Version}");
                org["contact"].ShouldBeNull($"pre-R5 should not emit ExtendedContactDetail contact in {schema.Version}");
            }
        }
    }

    [Fact]
    public void GivenImmunization_WhenGeneratedAcrossAllVersions_ThenManufacturerMatchesTypeShape()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing Immunization.manufacturer with {schema.Version}");

            var scenario = new ScenarioBuilder(schema)
                .WithPatient()
                .AddEncounter("Wellness visit")
                .AddImmunization(ImmunizationState.Covid19Pfizer())
                .Build();

            var manufacturer = scenario.Immunizations[0].MutableNode["manufacturer"];
            manufacturer.ShouldNotBeNull($"manufacturer should exist in {schema.Version}");

            // R5 changed manufacturer from Reference(Organization) to CodeableReference(Organization):
            // the display name moves from Reference.display to CodeableReference.concept.text.
            if (schema.Version >= FhirVersion.R5)
            {
                manufacturer!["display"].ShouldBeNull($"{schema.Version} CodeableReference has no direct '.display'");
                manufacturer["concept"]?["text"]?.GetValue<string>()
                    .ShouldBe("Pfizer Inc.", $"manufacturer name should be under concept.text in {schema.Version}");
            }
            else
            {
                manufacturer!["display"]?.GetValue<string>()
                    .ShouldBe("Pfizer Inc.", $"manufacturer name should be under Reference.display in {schema.Version}");
            }
        }
    }

    [Fact]
    public void GivenProcedureWithReasonCode_WhenGeneratedAcrossAllVersions_ThenUsesVersionCorrectReasonShape()
    {
        var reasonCode = new Ignixa.FhirFakes.Scenarios.Codes.FhirCode("http://snomed.info/sct", "22298006", "Myocardial infarction");

        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing Procedure.reason with {schema.Version}");

            var scenario = new ScenarioBuilder(schema)
                .WithPatient()
                .AddEncounter("Surgery")
                .AddProcedure(new ProcedureState
                {
                    Name = "Reasoned_Procedure",
                    Code = Procedures.CABG,
                    ReasonCode = reasonCode
                })
                .Build();

            var procedure = scenario.Procedures[0].MutableNode;

            // R5 merged Procedure.reasonCode (CodeableConcept[]) and reasonReference (Reference[])
            // into Procedure.reason (CodeableReference[]), where codes sit under ".concept".
            if (schema.Version >= FhirVersion.R5)
            {
                procedure["reasonCode"].ShouldBeNull($"R5+ dropped Procedure.reasonCode in {schema.Version}");
                var reason = procedure["reason"] as JsonArray;
                reason.ShouldNotBeNull($"R5+ should use Procedure.reason in {schema.Version}");
                var code = reason![0]?["concept"]?["coding"]?[0]?["code"]?.GetValue<string>();
                code.ShouldBe("22298006", $"reason.concept should carry the code in {schema.Version}");
            }
            else
            {
                procedure["reason"].ShouldBeNull($"pre-R5 has no Procedure.reason in {schema.Version}");
                var reasonCodeArray = procedure["reasonCode"] as JsonArray;
                reasonCodeArray.ShouldNotBeNull($"pre-R5 should use Procedure.reasonCode in {schema.Version}");
                var code = reasonCodeArray![0]?["coding"]?[0]?["code"]?.GetValue<string>();
                code.ShouldBe("22298006", $"reasonCode should carry the code in {schema.Version}");
            }
        }
    }
}
