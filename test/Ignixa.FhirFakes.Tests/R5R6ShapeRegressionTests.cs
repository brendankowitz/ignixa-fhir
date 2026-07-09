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
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Xunit.Abstractions;
using FhirCode = Ignixa.FhirFakes.Scenarios.Codes.FhirCode;

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

            var participant = ((IMutableJsonNode)scenario.Encounters[0]).MutableNode["participant"]?[0];
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

            var status = ((IMutableJsonNode)scenario.Encounters[0]).MutableNode["status"]?.GetValue<string>();

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

            var org = ((IMutableJsonNode)scenario.Organizations[0]).MutableNode;

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

            var manufacturer = ((IMutableJsonNode)scenario.Immunizations[0]).MutableNode["manufacturer"];
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

            var procedure = ((IMutableJsonNode)scenario.Procedures[0]).MutableNode;

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

    [Fact]
    public void GivenResources_WhenComposedToTransactionBundle_ThenEntriesUsePostMethodAndResourceTypeUrl()
    {
        var resources = new List<ResourceJsonNode>
        {
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"patient-1"}"""),
            ResourceJsonNode.Parse("""{"resourceType":"Encounter","id":"encounter-1"}""")
        };

        var bundle = ResourceBundleComposer.ToTransactionBundle(resources);
        var entries = ((IMutableJsonNode)bundle).MutableNode["entry"]!.AsArray();

        entries.Count.ShouldBe(resources.Count);
        for (var i = 0; i < resources.Count; i++)
        {
            var entry = entries[i]!;
            var resource = resources[i];

            entry["fullUrl"]?.GetValue<string>().ShouldBe($"urn:uuid:{resource.Id}");
            entry["request"]?["method"]?.GetValue<string>().ShouldBe("POST");
            entry["request"]?["url"]?.GetValue<string>().ShouldBe(resource.ResourceType);
        }
    }

    [Fact]
    public void GivenResources_WhenComposedToBatchBundle_ThenEntriesUsePutMethodAndResourceTypeSlashIdUrl()
    {
        var resources = new List<ResourceJsonNode>
        {
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"patient-1"}"""),
            ResourceJsonNode.Parse("""{"resourceType":"Encounter","id":"encounter-1"}""")
        };

        var bundle = ResourceBundleComposer.ToBatchBundle(resources);
        var entries = ((IMutableJsonNode)bundle).MutableNode["entry"]!.AsArray();

        entries.Count.ShouldBe(resources.Count);
        for (var i = 0; i < resources.Count; i++)
        {
            var entry = entries[i]!;
            var resource = resources[i];
            var expectedUrl = $"{resource.ResourceType}/{resource.Id}";

            entry["fullUrl"]?.GetValue<string>().ShouldBe(expectedUrl);
            entry["request"]?["method"]?.GetValue<string>().ShouldBe("PUT");
            entry["request"]?["url"]?.GetValue<string>().ShouldBe(expectedUrl);
        }
    }

    [Fact]
    public void GivenMedicationRequestWithReasonConditionReference_WhenGeneratedAcrossAllVersions_ThenUsesVersionCorrectReasonReferenceShape()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing MedicationRequest.reason (condition reference) with {schema.Version}");

            var scenario = new ScenarioBuilder(schema)
                .WithPatient()
                .AddEncounter("Follow-up")
                .AddConditionOnset(FhirCode.Conditions.DiabetesType2, assignToAttribute: "test_condition")
                .AddMedicationOrder(new MedicationOrderState
                {
                    Name = "Reasoned_Medication",
                    Code = FhirCode.Medications.Metformin500mg,
                    ReasonConditionAttribute = "test_condition"
                })
                .Build();

            var conditionId = scenario.Conditions[0].Id;
            var medication = ((IMutableJsonNode)scenario.Medications[0]).MutableNode;

            // R5 merged MedicationRequest.reasonCode/reasonReference into a single "reason"
            // element typed CodeableReference; a referenced Condition lives under ".reference",
            // not ".concept" (both are schema-legal, so only asserting the value here would miss
            // a swap of the two branches).
            if (schema.Version >= FhirVersion.R5)
            {
                medication["reasonReference"].ShouldBeNull($"R5+ has no MedicationRequest.reasonReference in {schema.Version}");
                var reason = medication["reason"] as JsonArray;
                reason.ShouldNotBeNull($"R5+ should use MedicationRequest.reason in {schema.Version}");
                reason![0]?["concept"].ShouldBeNull($"a condition reference must not be under .concept in {schema.Version}");
                var reference = reason[0]?["reference"]?["reference"]?.GetValue<string>();
                // KNOWN GAP (not fixed here): the generated reference metadata
                // (Ignixa.Specification/Generated/*ReferenceMetadata.g.cs) has no entry for R5+'s
                // "reason" field on MedicationRequest/Procedure — CodeableReference-typed fields
                // appear to be missing wholesale from the metadata generator's output, so
                // ReferenceRewriterService never rewrites this nested reference. Pre-R5's flat
                // Reference-typed reasonReference IS covered and correctly rewritten (see the else
                // branch below). This asserts the current, unrewritten value rather than papering
                // over the inconsistency; fixing the metadata generator is a separate, larger task.
                reference.ShouldBe($"Condition/{conditionId}", $"reason.reference in {schema.Version} (unrewritten — see comment above)");
            }
            else
            {
                medication["reason"].ShouldBeNull($"pre-R5 has no MedicationRequest.reason in {schema.Version}");
                var reasonReference = medication["reasonReference"] as JsonArray;
                reasonReference.ShouldNotBeNull($"pre-R5 should use MedicationRequest.reasonReference in {schema.Version}");
                var reference = reasonReference![0]?["reference"]?.GetValue<string>();
                reference.ShouldBe($"urn:uuid:{conditionId}", $"reasonReference should point at the condition in {schema.Version}");
            }
        }
    }

    [Fact]
    public void GivenProcedureWithReasonConditionReference_WhenGeneratedAcrossAllVersions_ThenUsesVersionCorrectReasonReferenceShape()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing Procedure.reason (condition reference) with {schema.Version}");

            var scenario = new ScenarioBuilder(schema)
                .WithPatient()
                .AddEncounter("Follow-up")
                .AddConditionOnset(FhirCode.Conditions.Hypertension, assignToAttribute: "test_condition")
                .AddProcedure(new ProcedureState
                {
                    Name = "Reasoned_Procedure",
                    Code = Procedures.CardiacCatheterization,
                    ReasonConditionAttribute = "test_condition"
                })
                .Build();

            var conditionId = scenario.Conditions[0].Id;
            var procedure = ((IMutableJsonNode)scenario.Procedures[0]).MutableNode;

            // Same CodeableReference merge as MedicationRequest: a referenced Condition must
            // land under Procedure.reason[].reference, not .concept.
            if (schema.Version >= FhirVersion.R5)
            {
                procedure["reasonReference"].ShouldBeNull($"R5+ has no Procedure.reasonReference in {schema.Version}");
                var reason = procedure["reason"] as JsonArray;
                reason.ShouldNotBeNull($"R5+ should use Procedure.reason in {schema.Version}");
                reason![0]?["concept"].ShouldBeNull($"a condition reference must not be under .concept in {schema.Version}");
                var reference = reason[0]?["reference"]?["reference"]?.GetValue<string>();
                // KNOWN GAP (not fixed here): the generated reference metadata
                // (Ignixa.Specification/Generated/*ReferenceMetadata.g.cs) has no entry for R5+'s
                // "reason" field on MedicationRequest/Procedure — CodeableReference-typed fields
                // appear to be missing wholesale from the metadata generator's output, so
                // ReferenceRewriterService never rewrites this nested reference. Pre-R5's flat
                // Reference-typed reasonReference IS covered and correctly rewritten (see the else
                // branch below). This asserts the current, unrewritten value rather than papering
                // over the inconsistency; fixing the metadata generator is a separate, larger task.
                reference.ShouldBe($"Condition/{conditionId}", $"reason.reference in {schema.Version} (unrewritten — see comment above)");
            }
            else
            {
                procedure["reason"].ShouldBeNull($"pre-R5 has no Procedure.reason in {schema.Version}");
                var reasonReference = procedure["reasonReference"] as JsonArray;
                reasonReference.ShouldNotBeNull($"pre-R5 should use Procedure.reasonReference in {schema.Version}");
                var reference = reasonReference![0]?["reference"]?.GetValue<string>();
                reference.ShouldBe($"urn:uuid:{conditionId}", $"reasonReference should point at the condition in {schema.Version}");
            }
        }
    }

    [Theory]
    [InlineData("arrived")]
    [InlineData("triaged")]
    public void GivenEncounterWithArrivedOrTriagedStatus_WhenGeneratedAcrossAllVersions_ThenMapsToInProgressOnR5Plus(string status)
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing Encounter.status '{status}' mapping with {schema.Version}");

            var scenario = new ScenarioBuilder(schema)
                .WithPatient()
                .AddState(new EncounterState
                {
                    Name = $"{status}_Encounter",
                    Status = status
                })
                .Build();

            var mappedStatus = ((IMutableJsonNode)scenario.Encounters[0]).MutableNode["status"]?.GetValue<string>();

            // R5 dropped "arrived"/"triaged" from the encounter-status value set in favor of "in-progress".
            var expected = schema.Version >= FhirVersion.R5 ? "in-progress" : status;
            mappedStatus.ShouldBe(expected, $"status should be '{expected}' in {schema.Version}");
        }
    }

    [Fact]
    public void GivenEncounterWithOnLeaveStatus_WhenGeneratedAcrossAllVersions_ThenMapsToOnHoldOnR5Plus()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing Encounter.status 'onleave' mapping with {schema.Version}");

            var scenario = new ScenarioBuilder(schema)
                .WithPatient()
                .AddState(new EncounterState
                {
                    Name = "OnLeave_Encounter",
                    Status = "onleave"
                })
                .Build();

            var status = ((IMutableJsonNode)scenario.Encounters[0]).MutableNode["status"]?.GetValue<string>();

            // R5 dropped "onleave" from the encounter-status value set in favor of "on-hold".
            var expected = schema.Version >= FhirVersion.R5 ? "on-hold" : "onleave";
            status.ShouldBe(expected, $"status should be '{expected}' in {schema.Version}");
        }
    }
}
