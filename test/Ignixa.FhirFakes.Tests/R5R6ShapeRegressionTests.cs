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
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Xunit.Abstractions;
using FhirCode = Ignixa.FhirFakes.Scenarios.Codes.FhirCode;
using Ignixa.Serialization.TestSupport;

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

            var participant = scenario.Encounters[0].MutableNode()["participant"]?[0];
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

            var status = scenario.Encounters[0].MutableNode()["status"]?.GetValue<string>();

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

            var org = scenario.Organizations[0].MutableNode();

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

            var manufacturer = scenario.Immunizations[0].MutableNode()["manufacturer"];
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

            var procedure = scenario.Procedures[0].MutableNode();

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
        var entries = bundle.MutableNode()["entry"]!.AsArray();

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
        var entries = bundle.MutableNode()["entry"]!.AsArray();

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
            var medication = scenario.Medications[0].MutableNode();

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
            var procedure = scenario.Procedures[0].MutableNode();

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

    [Fact]
    public void GivenProcedureWithComplication_WhenGeneratedAcrossAllVersions_ThenUsesVersionCorrectComplicationShape()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing Procedure.complication with {schema.Version}");

            var scenario = new ScenarioBuilder(schema)
                .WithPatient()
                .AddEncounter("Surgery")
                .AddProcedure(new ProcedureState
                {
                    Name = "Complicated_Procedure",
                    Code = Procedures.CABG,
                    Complication = "Post-operative bleeding"
                })
                .Build();

            var procedureNode = scenario.Procedures[0].MutableNode();
            var complication = procedureNode["complication"]?[0];
            complication.ShouldNotBeNull($"complication should exist in {schema.Version}");

            // R5 changed Procedure.complication from CodeableConcept to CodeableReference: the
            // coded value moves from .text directly to .concept.text. Note this boundary is R5,
            // unlike Procedure.outcome/followUp which don't switch until R6.
            if (schema.Version >= FhirVersion.R5)
            {
                complication!["text"].ShouldBeNull($"{schema.Version} CodeableReference has no direct '.text'");
                complication["concept"]?["text"]?.GetValue<string>()
                    .ShouldBe("Post-operative bleeding", $"complication text should be under concept.text in {schema.Version}");
            }
            else
            {
                complication!["text"]?.GetValue<string>()
                    .ShouldBe("Post-operative bleeding", $"complication text should be direct in {schema.Version}");
            }

            AssertNoValidationErrors(procedureNode, schema);
        }
    }

    [Fact]
    public void GivenProcedureWithOutcomeAndFollowUp_WhenGeneratedAcrossAllVersions_ThenUsesVersionCorrectShape()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing Procedure.outcome/followUp with {schema.Version}");

            var scenario = new ScenarioBuilder(schema)
                .WithPatient()
                .AddEncounter("Surgery")
                .AddProcedure(new ProcedureState
                {
                    Name = "Outcome_Procedure",
                    Code = Procedures.CABG,
                    Outcome = "Successful",
                    FollowUp = "Return in 2 weeks"
                })
                .Build();

            var procedureNode = scenario.Procedures[0].MutableNode();
            // Pre-R6, outcome is 0..1 (a single object, not an array); followUp has always been 0..*.
            var outcome = schema.Version >= FhirVersion.R6 ? procedureNode["outcome"]?[0] : procedureNode["outcome"];
            var followUp = procedureNode["followUp"]?[0];
            outcome.ShouldNotBeNull($"outcome should exist in {schema.Version}");
            followUp.ShouldNotBeNull($"followUp should exist in {schema.Version}");

            // R6 changed both Procedure.outcome and Procedure.followUp from CodeableConcept to
            // CodeableReference: the coded value moves from .text directly to .concept.text. Note
            // this boundary is R6, unlike Procedure.complication which switches at R5.
            if (schema.Version >= FhirVersion.R6)
            {
                outcome!["text"].ShouldBeNull($"{schema.Version} CodeableReference has no direct '.text'");
                outcome["concept"]?["text"]?.GetValue<string>()
                    .ShouldBe("Successful", $"outcome text should be under concept.text in {schema.Version}");
                followUp!["text"].ShouldBeNull($"{schema.Version} CodeableReference has no direct '.text'");
                followUp["concept"]?["text"]?.GetValue<string>()
                    .ShouldBe("Return in 2 weeks", $"followUp text should be under concept.text in {schema.Version}");
            }
            else
            {
                outcome!["text"]?.GetValue<string>()
                    .ShouldBe("Successful", $"outcome text should be direct in {schema.Version}");
                followUp!["text"]?.GetValue<string>()
                    .ShouldBe("Return in 2 weeks", $"followUp text should be direct in {schema.Version}");
            }

            AssertNoValidationErrors(procedureNode, schema);
        }
    }

    [Fact]
    public void GivenCarePlanWithRelatedCondition_WhenGeneratedAcrossAllVersions_ThenUsesVersionCorrectAddressesShape()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing CarePlan.addresses with {schema.Version}");

            var scenario = new ScenarioBuilder(schema)
                .WithPatient()
                .AddConditionOnset(FhirCode.Conditions.DiabetesType2, assignToAttribute: "care_plan_condition")
                .AddCarePlan(new CarePlanState
                {
                    Name = "Conditioned_CarePlan",
                    Title = "Diabetes Management",
                    RelatedConditionAttribute = "care_plan_condition"
                })
                .Build();

            var conditionId = scenario.Conditions[0].Id;
            var carePlanNode = scenario.CarePlans[0].MutableNode();
            var addresses = carePlanNode["addresses"]?[0];
            addresses.ShouldNotBeNull($"addresses should exist in {schema.Version}");

            // R5 changed CarePlan.addresses from Reference to CodeableReference: the reference
            // value moves under a nested "reference" object rather than sitting directly on the
            // array entry.
            if (schema.Version >= FhirVersion.R5)
            {
                addresses!["reference"].ShouldNotBeNull($"{schema.Version} CodeableReference wraps 'reference'");
                var reference = addresses["reference"]?["reference"]?.GetValue<string>();
                // KNOWN GAP (see the MedicationRequest/Procedure reason tests above): the generated
                // reference metadata has a "CarePlan.addresses" entry in STU3/R4/R4B, but it was
                // dropped entirely in R5/R6 (verified against *ReferenceMetadata.g.cs) rather than
                // updated for the new CodeableReference nesting, so ReferenceRewriterService never
                // rewrites this nested reference to urn:uuid in R5+. This asserts the current,
                // unrewritten value rather than papering over the inconsistency.
                reference.ShouldBe($"Condition/{conditionId}", $"addresses.reference.reference in {schema.Version} (unrewritten — see comment above)");
            }
            else
            {
                var reference = addresses!["reference"]?.GetValue<string>();
                reference.ShouldBe($"urn:uuid:{conditionId}", $"addresses.reference should point at the condition in {schema.Version}");
            }

            // Only validated for R5+, where this PR's fix applies. Pre-R5 CarePlan generation has
            // an unrelated, pre-existing issue (CarePlan.created isn't defined in STU3) that's out
            // of scope here.
            if (schema.Version >= FhirVersion.R5)
            {
                AssertNoValidationErrors(carePlanNode, schema);
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

            var mappedStatus = scenario.Encounters[0].MutableNode()["status"]?.GetValue<string>();

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

            var status = scenario.Encounters[0].MutableNode()["status"]?.GetValue<string>();

            // R5 dropped "onleave" from the encounter-status value set in favor of "on-hold".
            var expected = schema.Version >= FhirVersion.R5 ? "on-hold" : "onleave";
            status.ShouldBe(expected, $"status should be '{expected}' in {schema.Version}");
        }
    }

    private static void AssertNoValidationErrors(JsonNode resourceNode, IFhirSchemaProvider schemaProvider)
    {
        var sourceNode = JsonNodeSourceNode.Create(resourceNode);
        var resourceType = sourceNode.ResourceType ?? sourceNode.Name;
        var canonicalUrl = $"http://hl7.org/fhir/StructureDefinition/{resourceType}";
        var resolver = new CachedValidationSchemaResolver(new StructureDefinitionSchemaResolver(schemaProvider));
        var schema = resolver.GetSchema(canonicalUrl);
        schema.ShouldNotBeNull($"no schema found for resource type '{resourceType}' in {schemaProvider.Version}");

        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var element = sourceNode.ToElement(schemaProvider);
        var result = schema!.Validate(element, settings, ValidationState.ForRoot(element));

        var errors = result.Issues
            .Where(i => i.Severity is IssueSeverity.Error or IssueSeverity.Fatal)
            .Select(i => $"@{i.Path}: {i.Message}")
            .ToList();
        errors.ShouldBeEmpty($"{resourceType} should pass schema validation in {schemaProvider.Version}, but got: {string.Join("; ", errors)}");
    }
}
