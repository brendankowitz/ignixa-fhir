// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using FluentAssertions;
using Ignixa.Abstractions;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Serialization;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Xunit.Abstractions;

namespace Ignixa.FhirFakes.Tests;

/// <summary>
/// Tests FHIR version compatibility for custom state implementations.
/// Verifies that DiagnosticReport, Immunization, AllergyIntolerance, and Procedure states
/// work correctly across R4, R4B, R5, and STU3.
/// </summary>
public class CrossVersionCompatibilityTests
{
    private readonly ITestOutputHelper _output;
    private readonly List<IFhirSchemaProvider> _schemaProviders;

    public CrossVersionCompatibilityTests(ITestOutputHelper output)
    {
        _output = output;
        _schemaProviders =
        [
            new R4CoreSchemaProvider(),
            new R4BCoreSchemaProvider(),
            new R5CoreSchemaProvider(),
            new STU3CoreSchemaProvider()
        ];
    }

    #region DiagnosticReport Compatibility Tests

    [Fact]
    public void GivenDiagnosticReport_WhenGeneratedAcrossAllVersions_ThenAllSucceed()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing DiagnosticReport with {schema.Version} ({schema.FullVersion})");

            // Act
            var exception = Record.Exception(() =>
            {
                var scenario = new ScenarioBuilder(schema)
                    .WithPatient()
                    .AddEncounter("Lab work")
                    .AddComprehensiveMetabolicPanel()
                    .Build();

                // Assert basic structure
                scenario.DiagnosticReports.Should().HaveCount(1);
                scenario.DiagnosticReports[0].ResourceType.Should().Be("DiagnosticReport");
                scenario.DiagnosticReports[0].Id.Should().NotBeNullOrEmpty();
            });

            exception.Should().BeNull($"DiagnosticReport should work with {schema.Version}");
        }
    }

    [Fact]
    public void GivenDiagnosticReport_WhenGeneratedAcrossAllVersions_ThenHasRequiredFields()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing DiagnosticReport required fields with {schema.Version}");

            // Act
            var scenario = new ScenarioBuilder(schema)
                .WithPatient()
                .AddEncounter("Lab work")
                .AddComprehensiveMetabolicPanel()
                .Build();

            var report = scenario.DiagnosticReports[0];

            // Assert - Common required fields across all versions
            report.MutableNode["status"].Should().NotBeNull($"status is required in {schema.Version}");
            report.MutableNode["code"].Should().NotBeNull($"code is required in {schema.Version}");

            // subject is required in R4/R4B/R5, but not STU3 (STU3 uses Patient + Encounter)
            if (schema.Version != FhirVersion.Stu3)
            {
                report.MutableNode["subject"].Should().NotBeNull($"subject is required in {schema.Version}");
            }
        }
    }

    [Fact]
    public void GivenDiagnosticReportWithObservations_WhenGeneratedAcrossAllVersions_ThenCreatesLinkedObservations()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing DiagnosticReport observations with {schema.Version}");

            // Act
            var scenario = new ScenarioBuilder(schema)
                .WithPatient()
                .AddEncounter("Lab work")
                .AddComprehensiveMetabolicPanel()
                .Build();

            // Assert
            scenario.Observations.Should().HaveCount(14, $"CMP should have 14 observations in {schema.Version}");
            var report = scenario.DiagnosticReports[0];
            var results = report.MutableNode["result"] as System.Text.Json.Nodes.JsonArray;
            results.Should().NotBeNull($"result field should exist in {schema.Version}");
            results!.Count.Should().Be(14, $"should link to all 14 observations in {schema.Version}");
        }
    }

    [Fact]
    public void GivenImagingReport_WhenGeneratedAcrossAllVersions_ThenAllSucceed()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing imaging report with {schema.Version}");

            // Act
            var scenario = new ScenarioBuilder(schema)
                .WithPatient()
                .AddEncounter("Imaging")
                .AddChestXRay("Clear lungs, no abnormalities.")
                .Build();

            // Assert
            scenario.DiagnosticReports.Should().HaveCount(1);
            var report = scenario.DiagnosticReports[0];
            var categoryCode = report.MutableNode["category"]?[0]?["coding"]?[0]?["code"]?.GetValue<string>();
            categoryCode.Should().Be("RAD", $"should have radiology category in {schema.Version}");

            var conclusion = report.MutableNode["conclusion"]?.GetValue<string>();
            conclusion.Should().Be("Clear lungs, no abnormalities.", $"should have conclusion in {schema.Version}");
        }
    }

    #endregion

    #region Immunization Compatibility Tests

    [Fact]
    public void GivenImmunization_WhenGeneratedAcrossAllVersions_ThenAllSucceed()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing Immunization with {schema.Version} ({schema.FullVersion})");

            // Act
            var exception = Record.Exception(() =>
            {
                var scenario = new ScenarioBuilder(schema)
                    .WithPatient()
                    .AddEncounter("Wellness visit")
                    .AddInfluenzaVaccine()
                    .Build();

                // Assert basic structure
                scenario.Immunizations.Should().HaveCount(1);
                scenario.Immunizations[0].ResourceType.Should().Be("Immunization");
                scenario.Immunizations[0].Id.Should().NotBeNullOrEmpty();
            });

            exception.Should().BeNull($"Immunization should work with {schema.Version}");
        }
    }

    [Fact]
    public void GivenImmunization_WhenGeneratedAcrossAllVersions_ThenHasRequiredFields()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing Immunization required fields with {schema.Version}");

            // Act
            var scenario = new ScenarioBuilder(schema)
                .WithPatient()
                .AddEncounter("Wellness visit")
                .AddInfluenzaVaccine()
                .Build();

            var immunization = scenario.Immunizations[0];

            // Assert - Common required fields across all versions
            immunization.MutableNode["status"].Should().NotBeNull($"status is required in {schema.Version}");
            immunization.MutableNode["vaccineCode"].Should().NotBeNull($"vaccineCode is required in {schema.Version}");
            immunization.MutableNode["patient"].Should().NotBeNull($"patient is required in {schema.Version}");

            // occurrenceDateTime is required in R4/R4B/R5
            if (schema.Version != FhirVersion.Stu3)
            {
                immunization.MutableNode["occurrenceDateTime"].Should().NotBeNull($"occurrenceDateTime is required in {schema.Version}");
            }
        }
    }

    [Fact]
    public void GivenImmunizationWithProtocol_WhenGeneratedAcrossAllVersions_ThenHasProtocolApplied()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing Immunization protocol with {schema.Version}");

            // Act
            var scenario = new ScenarioBuilder(schema)
                .WithPatient()
                .AddEncounter("Wellness visit")
                .AddImmunization(ImmunizationState.MMRDose1())
                .Build();

            var immunization = scenario.Immunizations[0];

            // Assert - protocolApplied should exist (R4+) or vaccinationProtocol (STU3)
            if (schema.Version == FhirVersion.Stu3)
            {
                // STU3 uses vaccinationProtocol instead of protocolApplied
                immunization.MutableNode["vaccinationProtocol"].Should().NotBeNull($"vaccinationProtocol should exist in {schema.Version}");
                var doseNumber = immunization.MutableNode["vaccinationProtocol"]?[0]?["doseSequence"]?.GetValue<int>();
                doseNumber.Should().Be(1, $"dose number should be 1 in {schema.Version}");
            }
            else
            {
                immunization.MutableNode["protocolApplied"].Should().NotBeNull($"protocolApplied should exist in {schema.Version}");
                var doseNumber = immunization.MutableNode["protocolApplied"]?[0]?["doseNumberPositiveInt"]?.GetValue<int>();
                doseNumber.Should().Be(1, $"dose number should be 1 in {schema.Version}");
            }
        }
    }

    [Fact]
    public void GivenImmunization_WhenGeneratedWithSTU3_ThenUsesSTU3FieldNames()
    {
        // Arrange
        var stu3Schema = new STU3CoreSchemaProvider();
        _output.WriteLine("Testing Immunization STU3-specific field names");

        // Act
        var scenario = new ScenarioBuilder(stu3Schema)
            .WithPatient()
            .AddEncounter("Wellness visit")
            .AddImmunization(ImmunizationState.MMRDose1())
            .Build();

        var immunization = scenario.Immunizations[0];

        // Assert - STU3 uses vaccinationProtocol instead of protocolApplied
        immunization.MutableNode.AsObject().Should().ContainKey("vaccinationProtocol", "STU3 should use 'vaccinationProtocol'");
        immunization.MutableNode.AsObject().Should().NotContainKey("protocolApplied", "STU3 should NOT use 'protocolApplied' (R4+ field)");

        // Assert - STU3 uses doseSequence instead of doseNumberPositiveInt
        var protocol = immunization.MutableNode["vaccinationProtocol"]?[0];
        protocol.Should().NotBeNull("vaccinationProtocol should have at least one entry");
        protocol!.AsObject().Should().ContainKey("doseSequence", "STU3 should use 'doseSequence'");
        protocol.AsObject().Should().NotContainKey("doseNumberPositiveInt", "STU3 should NOT use 'doseNumberPositiveInt' (R4+ field)");

        // Assert - STU3 should NOT have seriesDosesPositiveInt
        protocol.AsObject().Should().NotContainKey("seriesDosesPositiveInt", "STU3 doesn't have 'seriesDosesPositiveInt' field");
    }

    [Fact]
    public void GivenImmunization_WhenGeneratedWithR4_ThenUsesR4FieldNames()
    {
        // Arrange
        var r4Schema = new R4CoreSchemaProvider();
        _output.WriteLine("Testing Immunization R4-specific field names");

        // Act
        var scenario = new ScenarioBuilder(r4Schema)
            .WithPatient()
            .AddEncounter("Wellness visit")
            .AddImmunization(ImmunizationState.MMRDose1())
            .Build();

        var immunization = scenario.Immunizations[0];

        // Assert - R4 uses protocolApplied instead of vaccinationProtocol
        immunization.MutableNode.AsObject().Should().ContainKey("protocolApplied", "R4 should use 'protocolApplied'");
        immunization.MutableNode.AsObject().Should().NotContainKey("vaccinationProtocol", "R4 should NOT use 'vaccinationProtocol' (STU3 field)");

        // Assert - R4 uses doseNumberPositiveInt instead of doseSequence
        var protocol = immunization.MutableNode["protocolApplied"]?[0];
        protocol.Should().NotBeNull("protocolApplied should have at least one entry");
        protocol!.AsObject().Should().ContainKey("doseNumberPositiveInt", "R4 should use 'doseNumberPositiveInt'");
        protocol.AsObject().Should().NotContainKey("doseSequence", "R4 should NOT use 'doseSequence' (STU3 field)");

        // Assert - R4 should have seriesDosesPositiveInt when SeriesDosesRecommended is set
        protocol.AsObject().Should().ContainKey("seriesDosesPositiveInt", "R4 should have 'seriesDosesPositiveInt'");
    }

    [Fact]
    public void GivenImmunization_WhenGeneratedWithR4B_ThenUsesR4FieldNames()
    {
        // Arrange
        var r4bSchema = new R4BCoreSchemaProvider();
        _output.WriteLine("Testing Immunization R4B-specific field names");

        // Act
        var scenario = new ScenarioBuilder(r4bSchema)
            .WithPatient()
            .AddEncounter("Wellness visit")
            .AddImmunization(ImmunizationState.MMRDose1())
            .Build();

        var immunization = scenario.Immunizations[0];

        // Assert - R4B uses same field names as R4
        immunization.MutableNode.AsObject().Should().ContainKey("protocolApplied", "R4B should use 'protocolApplied'");
        var protocol = immunization.MutableNode["protocolApplied"]?[0];
        protocol.Should().NotBeNull();
        protocol!.AsObject().Should().ContainKey("doseNumberPositiveInt", "R4B should use 'doseNumberPositiveInt'");
    }

    [Fact]
    public void GivenImmunization_WhenGeneratedWithR5_ThenUsesR4FieldNames()
    {
        // Arrange
        var r5Schema = new R5CoreSchemaProvider();
        _output.WriteLine("Testing Immunization R5-specific field names");

        // Act
        var scenario = new ScenarioBuilder(r5Schema)
            .WithPatient()
            .AddEncounter("Wellness visit")
            .AddImmunization(ImmunizationState.MMRDose1())
            .Build();

        var immunization = scenario.Immunizations[0];

        // Assert - R5 uses same field names as R4
        immunization.MutableNode.AsObject().Should().ContainKey("protocolApplied", "R5 should use 'protocolApplied'");
        var protocol = immunization.MutableNode["protocolApplied"]?[0];
        protocol.Should().NotBeNull();
        protocol!.AsObject().Should().ContainKey("doseNumberPositiveInt", "R5 should use 'doseNumberPositiveInt'");
    }

    #endregion

    #region AllergyIntolerance Compatibility Tests

    [Fact]
    public void GivenAllergyIntolerance_WhenGeneratedAcrossAllVersions_ThenAllSucceed()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing AllergyIntolerance with {schema.Version} ({schema.FullVersion})");

            // Act
            var exception = Record.Exception(() =>
            {
                var scenario = new ScenarioBuilder(schema)
                    .WithPatient()
                    .AddPeanutAllergy()
                    .Build();

                // Assert basic structure
                scenario.Allergies.Should().HaveCount(1);
                scenario.Allergies[0].ResourceType.Should().Be("AllergyIntolerance");
                scenario.Allergies[0].Id.Should().NotBeNullOrEmpty();
            });

            exception.Should().BeNull($"AllergyIntolerance should work with {schema.Version}");
        }
    }

    [Fact]
    public void GivenAllergyIntolerance_WhenGeneratedAcrossAllVersions_ThenHasRequiredFields()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing AllergyIntolerance required fields with {schema.Version}");

            // Act
            var scenario = new ScenarioBuilder(schema)
                .WithPatient()
                .AddPeanutAllergy()
                .Build();

            var allergy = scenario.Allergies[0];

            // Assert - Required fields vary by version
            // R4+: clinicalStatus, verificationStatus, code, patient
            // STU3: patient only (other fields optional)

            allergy.MutableNode["patient"].Should().NotBeNull($"patient is required in {schema.Version}");

            if (schema.Version != FhirVersion.Stu3)
            {
                allergy.MutableNode["clinicalStatus"].Should().NotBeNull($"clinicalStatus is required in {schema.Version}");
                allergy.MutableNode["verificationStatus"].Should().NotBeNull($"verificationStatus is required in {schema.Version}");
                allergy.MutableNode["code"].Should().NotBeNull($"code is required in {schema.Version}");
            }
        }
    }

    [Fact]
    public void GivenAllergyWithReactions_WhenGeneratedAcrossAllVersions_ThenHasReactions()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing AllergyIntolerance reactions with {schema.Version}");

            // Act
            var scenario = new ScenarioBuilder(schema)
                .WithPatient()
                .AddPeanutAllergy()
                .Build();

            var allergy = scenario.Allergies[0];

            // Assert
            var reactions = allergy.MutableNode["reaction"] as System.Text.Json.Nodes.JsonArray;
            reactions.Should().NotBeNull($"reaction should exist in {schema.Version}");
            reactions!.Count.Should().BeGreaterThan(0, $"should have reactions in {schema.Version}");
        }
    }

    #endregion

    #region Procedure Compatibility Tests

    [Fact]
    public void GivenProcedure_WhenGeneratedAcrossAllVersions_ThenAllSucceed()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing Procedure with {schema.Version} ({schema.FullVersion})");

            // Act
            var exception = Record.Exception(() =>
            {
                var scenario = new ScenarioBuilder(schema)
                    .WithPatient()
                    .AddEncounter("Surgery")
                    .AddAppendectomy()
                    .Build();

                // Assert basic structure
                scenario.Procedures.Should().HaveCount(1);
                scenario.Procedures[0].ResourceType.Should().Be("Procedure");
                scenario.Procedures[0].Id.Should().NotBeNullOrEmpty();
            });

            exception.Should().BeNull($"Procedure should work with {schema.Version}");
        }
    }

    [Fact]
    public void GivenProcedure_WhenGeneratedAcrossAllVersions_ThenHasRequiredFields()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing Procedure required fields with {schema.Version}");

            // Act
            var scenario = new ScenarioBuilder(schema)
                .WithPatient()
                .AddEncounter("Surgery")
                .AddAppendectomy()
                .Build();

            var procedure = scenario.Procedures[0];

            // Assert - Required fields
            procedure.MutableNode["status"].Should().NotBeNull($"status is required in {schema.Version}");
            procedure.MutableNode["subject"].Should().NotBeNull($"subject is required in {schema.Version}");

            // code is optional in STU3 but required in R4+
            if (schema.Version != FhirVersion.Stu3)
            {
                procedure.MutableNode["code"].Should().NotBeNull($"code is required in {schema.Version}");
            }
        }
    }

    [Fact]
    public void GivenProcedureWithPerformedPeriod_WhenGeneratedAcrossAllVersions_ThenHasPerformedPeriod()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing Procedure performed/occurrence period with {schema.Version}");

            // Act
            var scenario = new ScenarioBuilder(schema)
                .WithPatient()
                .AddEncounter("Surgery")
                .AddAppendectomy()
                .Build();

            var procedure = scenario.Procedures[0];

            // Assert - performed/occurrence period should exist (version-aware field name)
            // STU3/R4/R4B: performedPeriod (from performed[x])
            // R5: occurrencePeriod (renamed from performed[x] to occurrence[x])
            var periodFieldName = schema.Version == FhirVersion.R5 ? "occurrencePeriod" : "performedPeriod";
            procedure.MutableNode[periodFieldName].Should().NotBeNull($"{periodFieldName} should exist in {schema.Version}");
            var start = procedure.MutableNode[periodFieldName]?["start"]?.GetValue<string>();
            start.Should().NotBeNullOrEmpty($"{periodFieldName}.start should be set in {schema.Version}");
        }
    }

    #endregion

    #region ServiceRequest Compatibility Tests

    // Note: ServiceRequest was introduced in FHIR R4. In STU3, it was called ProcedureRequest.
    // These tests skip STU3 as ServiceRequest is not available in that version.

    [Fact]
    public void GivenServiceRequest_WhenGeneratedAcrossR4Versions_ThenAllSucceed()
    {
        // ServiceRequest was introduced in R4, does not exist in STU3 (was called ProcedureRequest)
        var r4Providers = _schemaProviders.Where(p => p.Version != FhirVersion.Stu3).ToList();

        foreach (var schema in r4Providers)
        {
            _output.WriteLine($"Testing ServiceRequest with {schema.Version} ({schema.FullVersion})");

            // Act
            var exception = Record.Exception(() =>
            {
                var scenario = new ScenarioBuilder(schema)
                    .WithPatient()
                    .AddEncounter("Lab order")
                    .AddCBCOrder()
                    .Build();

                // Assert basic structure
                scenario.ServiceRequests.Should().HaveCount(1);
                scenario.ServiceRequests[0].ResourceType.Should().Be("ServiceRequest");
                scenario.ServiceRequests[0].Id.Should().NotBeNullOrEmpty();
            });

            exception.Should().BeNull($"ServiceRequest should work with {schema.Version}");
        }
    }

    [Fact]
    public void GivenServiceRequest_WhenGeneratedAcrossR4Versions_ThenHasRequiredFields()
    {
        var r4Providers = _schemaProviders.Where(p => p.Version != FhirVersion.Stu3).ToList();

        foreach (var schema in r4Providers)
        {
            _output.WriteLine($"Testing ServiceRequest required fields with {schema.Version}");

            // Act
            var scenario = new ScenarioBuilder(schema)
                .WithPatient()
                .AddEncounter("Lab order")
                .AddCBCOrder()
                .Build();

            var serviceRequest = scenario.ServiceRequests[0];

            // Assert - Required fields across all versions
            serviceRequest.MutableNode["status"].Should().NotBeNull($"status is required in {schema.Version}");
            serviceRequest.MutableNode["intent"].Should().NotBeNull($"intent is required in {schema.Version}");
            serviceRequest.MutableNode["subject"].Should().NotBeNull($"subject is required in {schema.Version}");
        }
    }

    [Fact]
    public void GivenServiceRequestWithPriority_WhenGeneratedAcrossR4Versions_ThenHasPriority()
    {
        var r4Providers = _schemaProviders.Where(p => p.Version != FhirVersion.Stu3).ToList();

        foreach (var schema in r4Providers)
        {
            _output.WriteLine($"Testing ServiceRequest priority with {schema.Version}");

            // Act
            var scenario = new ScenarioBuilder(schema)
                .WithPatient()
                .AddUrgentCBCOrder()
                .Build();

            var serviceRequest = scenario.ServiceRequests[0];

            // Assert - priority should exist across all versions
            var priority = serviceRequest.MutableNode["priority"]?.GetValue<string>();
            priority.Should().Be("urgent", $"priority should be 'urgent' in {schema.Version}");
        }
    }

    [Fact]
    public void GivenServiceRequestWithCategory_WhenGeneratedAcrossR4Versions_ThenHasCategory()
    {
        var r4Providers = _schemaProviders.Where(p => p.Version != FhirVersion.Stu3).ToList();

        foreach (var schema in r4Providers)
        {
            _output.WriteLine($"Testing ServiceRequest category with {schema.Version}");

            // Act
            var scenario = new ScenarioBuilder(schema)
                .WithPatient()
                .AddCBCOrder()
                .Build();

            var serviceRequest = scenario.ServiceRequests[0];

            // Assert - category should exist across all versions
            var category = serviceRequest.MutableNode["category"] as JsonArray;
            category.Should().NotBeNull($"category should exist in {schema.Version}");
            category!.Count.Should().BeGreaterThan(0, $"should have category entries in {schema.Version}");
        }
    }

    [Fact]
    public void GivenServiceRequest_WhenGeneratedAcrossR4Versions_ThenHasCode()
    {
        var r4Providers = _schemaProviders.Where(p => p.Version != FhirVersion.Stu3).ToList();

        foreach (var schema in r4Providers)
        {
            _output.WriteLine($"Testing ServiceRequest code with {schema.Version}");

            // Act
            var scenario = new ScenarioBuilder(schema)
                .WithPatient()
                .AddLipidPanelOrder()
                .Build();

            var serviceRequest = scenario.ServiceRequests[0];

            // Assert - code should exist with proper structure (version-aware)
            // R4/R4B: code is CodeableConcept -> code.coding
            // R5: code is CodeableReference -> code.concept.coding
            var code = serviceRequest.MutableNode["code"];
            code.Should().NotBeNull($"code should exist in {schema.Version}");

            JsonNode? coding;
            if (schema.Version == FhirVersion.R5)
            {
                // R5: CodeableReference with concept part
                coding = code?["concept"]?["coding"]?[0];
            }
            else
            {
                // R4/R4B: CodeableConcept directly
                coding = code?["coding"]?[0];
            }
            coding.Should().NotBeNull($"code.coding should exist in {schema.Version}");

            var system = coding?["system"]?.GetValue<string>();
            system.Should().Be("http://loinc.org", $"should use LOINC system in {schema.Version}");
        }
    }

    [Fact]
    public void GivenImagingServiceRequest_WhenGeneratedAcrossR4Versions_ThenAllSucceed()
    {
        var r4Providers = _schemaProviders.Where(p => p.Version != FhirVersion.Stu3).ToList();

        foreach (var schema in r4Providers)
        {
            _output.WriteLine($"Testing imaging ServiceRequest with {schema.Version}");

            // Act
            var exception = Record.Exception(() =>
            {
                var scenario = new ScenarioBuilder(schema)
                    .WithPatient()
                    .AddEncounter("Imaging referral")
                    .AddChestXRayOrder()
                    .Build();

                // Assert
                scenario.ServiceRequests.Should().HaveCount(1);
                var categoryCode = scenario.ServiceRequests[0].MutableNode["category"]?[0]?["coding"]?[0]?["code"]?.GetValue<string>();
                categoryCode.Should().Be("363679005", $"should have imaging category in {schema.Version}"); // Imaging
            });

            exception.Should().BeNull($"Imaging ServiceRequest should work with {schema.Version}");
        }
    }

    [Fact]
    public void GivenReferralServiceRequest_WhenGeneratedAcrossR4Versions_ThenAllSucceed()
    {
        var r4Providers = _schemaProviders.Where(p => p.Version != FhirVersion.Stu3).ToList();

        foreach (var schema in r4Providers)
        {
            _output.WriteLine($"Testing referral ServiceRequest with {schema.Version}");

            // Act
            var exception = Record.Exception(() =>
            {
                var scenario = new ScenarioBuilder(schema)
                    .WithPatient()
                    .AddEncounter("Specialist referral")
                    .AddCardiologyReferral()
                    .Build();

                // Assert
                scenario.ServiceRequests.Should().HaveCount(1);
                var categoryCode = scenario.ServiceRequests[0].MutableNode["category"]?[0]?["coding"]?[0]?["code"]?.GetValue<string>();
                categoryCode.Should().Be("3457005", $"should have referral category in {schema.Version}"); // Referral
            });

            exception.Should().BeNull($"Referral ServiceRequest should work with {schema.Version}");
        }
    }

    [Fact]
    public void GivenServiceRequest_WhenGeneratedWithSTU3_ThenThrowsNotSupportedException()
    {
        // ServiceRequest doesn't exist in STU3 (it was called ProcedureRequest)
        var stu3Schema = new STU3CoreSchemaProvider();
        _output.WriteLine("Testing ServiceRequest with STU3 (should fail - resource renamed)");

        // Act
        var exception = Record.Exception(() =>
        {
            var scenario = new ScenarioBuilder(stu3Schema)
                .WithPatient()
                .AddCBCOrder()
                .Build();
        });

        // Assert - Should throw because ServiceRequest doesn't exist in STU3
        exception.Should().NotBeNull("ServiceRequest should not work with STU3");
        exception.Should().BeOfType<ArgumentException>();
        _output.WriteLine($"Expected failure for STU3: {exception?.Message}");
    }

    #endregion

    #region MedicationRequest Compatibility Tests

    [Fact]
    public void GivenMedicationRequest_WhenGeneratedAcrossAllVersions_ThenAllSucceed()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing MedicationRequest with {schema.Version} ({schema.FullVersion})");

            // Act
            var exception = Record.Exception(() =>
            {
                var scenario = new ScenarioBuilder(schema)
                    .WithPatient()
                    .AddEncounter("Medication visit")
                    .AddMedicationOrder(MedicationOrderState.Metformin500mg())
                    .Build();

                // Assert basic structure
                scenario.Medications.Should().HaveCount(1);
                scenario.Medications[0].ResourceType.Should().Be("MedicationRequest");
                scenario.Medications[0].Id.Should().NotBeNullOrEmpty();
            });

            exception.Should().BeNull($"MedicationRequest should work with {schema.Version}");
        }
    }

    [Fact]
    public void GivenMedicationRequest_WhenGeneratedAcrossAllVersions_ThenHasRequiredFields()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing MedicationRequest required fields with {schema.Version}");

            // Act
            var scenario = new ScenarioBuilder(schema)
                .WithPatient()
                .AddEncounter("Medication visit")
                .AddMedicationOrder(MedicationOrderState.Lisinopril10mg())
                .Build();

            var medicationRequest = scenario.Medications[0];

            // Assert - Common required fields across all versions
            medicationRequest.MutableNode["status"].Should().NotBeNull($"status is required in {schema.Version}");
            medicationRequest.MutableNode["intent"].Should().NotBeNull($"intent is required in {schema.Version}");
            medicationRequest.MutableNode["subject"].Should().NotBeNull($"subject is required in {schema.Version}");
        }
    }

    [Fact]
    public void GivenMedicationRequest_WhenGeneratedAcrossAllVersions_ThenUsesMedicationField()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing MedicationRequest medication[x] field with {schema.Version}");

            // Act
            var scenario = new ScenarioBuilder(schema)
                .WithPatient()
                .AddMedicationOrder(MedicationOrderState.Atorvastatin20mg())
                .Build();

            var medicationRequest = scenario.Medications[0];

            // Assert - All FHIR versions use medication[x] choice element
            // STU3/R4/R4B/R5 all serialize as "medicationCodeableConcept" or "medicationReference"
            // (STU3 spec: medication[x][1..1]: CodeableConcept|Reference(Medication))
            var hasMedicationCodeableConcept = medicationRequest.MutableNode["medicationCodeableConcept"] != null;
            var hasMedicationReference = medicationRequest.MutableNode["medicationReference"] != null;

            (hasMedicationCodeableConcept || hasMedicationReference)
                .Should().BeTrue($"{schema.Version} should have medicationCodeableConcept or medicationReference");

            // If using CodeableConcept, verify structure
            if (hasMedicationCodeableConcept)
            {
                var coding = medicationRequest.MutableNode["medicationCodeableConcept"]?["coding"]?[0];
                coding.Should().NotBeNull($"medicationCodeableConcept should have coding in {schema.Version}");
                coding?["code"]?.GetValue<string>().Should().NotBeNullOrEmpty($"should have medication code in {schema.Version}");
            }
        }
    }

    [Fact]
    public void GivenMedicationRequestWithDosage_WhenGeneratedAcrossAllVersions_ThenHasDosageInstruction()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing MedicationRequest dosageInstruction with {schema.Version}");

            // Act
            var scenario = new ScenarioBuilder(schema)
                .WithPatient()
                .AddMedicationOrder(MedicationOrderState.Albuterol())
                .Build();

            var medicationRequest = scenario.Medications[0];

            // Assert - dosageInstruction should exist across all versions
            var dosageInstruction = medicationRequest.MutableNode["dosageInstruction"] as JsonArray;
            dosageInstruction.Should().NotBeNull($"dosageInstruction should exist in {schema.Version}");
            dosageInstruction!.Count.Should().BeGreaterThan(0, $"should have dosage entries in {schema.Version}");
        }
    }

    #endregion

    #region Version-Specific Issue Detection Tests

    [Fact]
    public void GivenAllStates_WhenGeneratedWithSTU3_ThenDocumentDifferences()
    {
        var stu3Schema = new STU3CoreSchemaProvider();
        _output.WriteLine("===== STU3 COMPATIBILITY ANALYSIS =====");

        // DiagnosticReport
        try
        {
            var scenario = new ScenarioBuilder(stu3Schema)
                .WithPatient()
                .AddComprehensiveMetabolicPanel()
                .Build();
            _output.WriteLine("✓ DiagnosticReport: Works in STU3");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"✗ DiagnosticReport: FAILED in STU3 - {ex.Message}");
        }

        // Immunization
        try
        {
            var scenario = new ScenarioBuilder(stu3Schema)
                .WithPatient()
                .AddInfluenzaVaccine()
                .Build();
            _output.WriteLine("✓ Immunization: Works in STU3");

            // Check for STU3-specific differences
            var imm = scenario.Immunizations[0];
            if (imm.MutableNode["protocolApplied"] != null)
            {
                _output.WriteLine("  ⚠ WARNING: Uses 'protocolApplied' (R4+), STU3 expects 'vaccinationProtocol'");
            }
            if (imm.MutableNode["primarySource"] != null)
            {
                _output.WriteLine("  ⚠ WARNING: Uses 'primarySource' (R4+), STU3 expects 'notGiven' + 'primarySource' differently");
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"✗ Immunization: FAILED in STU3 - {ex.Message}");
        }

        // AllergyIntolerance
        try
        {
            var scenario = new ScenarioBuilder(stu3Schema)
                .WithPatient()
                .AddPeanutAllergy()
                .Build();
            _output.WriteLine("✓ AllergyIntolerance: Works in STU3");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"✗ AllergyIntolerance: FAILED in STU3 - {ex.Message}");
        }

        // Procedure
        try
        {
            var scenario = new ScenarioBuilder(stu3Schema)
                .WithPatient()
                .AddAppendectomy()
                .Build();
            _output.WriteLine("✓ Procedure: Works in STU3");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"✗ Procedure: FAILED in STU3 - {ex.Message}");
        }

        // ServiceRequest - NOTE: Does not exist in STU3 (was called ProcedureRequest)
        try
        {
            var scenario = new ScenarioBuilder(stu3Schema)
                .WithPatient()
                .AddCBCOrder()
                .Build();
            _output.WriteLine("✓ ServiceRequest: Works in STU3 (unexpected!)");
        }
        catch (ArgumentException)
        {
            _output.WriteLine("✓ ServiceRequest: Correctly fails in STU3 (resource was called ProcedureRequest in STU3)");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"✗ ServiceRequest: FAILED in STU3 with unexpected error - {ex.Message}");
        }
    }

    [Fact]
    public void GivenAllStates_WhenGeneratedWithR5_ThenDocumentDifferences()
    {
        var r5Schema = new R5CoreSchemaProvider();
        _output.WriteLine("===== R5 COMPATIBILITY ANALYSIS =====");

        // DiagnosticReport
        try
        {
            var scenario = new ScenarioBuilder(r5Schema)
                .WithPatient()
                .AddComprehensiveMetabolicPanel()
                .Build();
            _output.WriteLine("✓ DiagnosticReport: Works in R5");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"✗ DiagnosticReport: FAILED in R5 - {ex.Message}");
        }

        // Immunization
        try
        {
            var scenario = new ScenarioBuilder(r5Schema)
                .WithPatient()
                .AddInfluenzaVaccine()
                .Build();
            _output.WriteLine("✓ Immunization: Works in R5");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"✗ Immunization: FAILED in R5 - {ex.Message}");
        }

        // AllergyIntolerance
        try
        {
            var scenario = new ScenarioBuilder(r5Schema)
                .WithPatient()
                .AddPeanutAllergy()
                .Build();
            _output.WriteLine("✓ AllergyIntolerance: Works in R5");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"✗ AllergyIntolerance: FAILED in R5 - {ex.Message}");
        }

        // Procedure
        try
        {
            var scenario = new ScenarioBuilder(r5Schema)
                .WithPatient()
                .AddAppendectomy()
                .Build();
            _output.WriteLine("✓ Procedure: Works in R5");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"✗ Procedure: FAILED in R5 - {ex.Message}");
        }

        // ServiceRequest
        try
        {
            var scenario = new ScenarioBuilder(r5Schema)
                .WithPatient()
                .AddCBCOrder()
                .Build();
            _output.WriteLine("✓ ServiceRequest: Works in R5");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"✗ ServiceRequest: FAILED in R5 - {ex.Message}");
        }
    }

    #endregion
}
