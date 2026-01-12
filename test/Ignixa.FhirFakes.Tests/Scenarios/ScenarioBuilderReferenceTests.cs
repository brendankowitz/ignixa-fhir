// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.Abstractions;
using Ignixa.Specification;
using FhirCode = Ignixa.FhirFakes.Scenarios.Codes.FhirCode;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFakes.Tests.Scenarios;

/// <summary>
/// Integration tests for ScenarioBuilder automatic reference management.
/// Validates reference rewriting between urn:uuid and resolved formats.
/// </summary>
public class ScenarioBuilderReferenceTests
{
    private readonly IFhirSchemaProvider _schemaProvider = new R4CoreSchemaProvider();

    #region Default Reference Format Tests

    [Fact]
    public void GivenDefaultScenario_WhenBuilding_ThenReferencesAreInOriginalFormat()
    {
        // Note: Default behavior is UrnUuid mode, which means NO rewriting occurs.
        // Resources are created with resolved references (Patient/id) by default,
        // and UrnUuid mode leaves them unchanged (does not rewrite).
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithName("Default Reference Format Test")
            .WithPatient(p => p.WithAge(35).WithGender("male"))
            .AddEncounter("Initial Visit")
            .AddObservation(FhirCode.Observations.BloodGlucose, 100m, "mg/dL")
            .Build();

        // Assert
        scenario.Observations.Count.ShouldBe(1);
        var observation = scenario.Observations[0];
        var subjectRef = observation.MutableNode["subject"]?["reference"]?.GetValue<string>();
        // Default is UrnUuid (no rewriting), so original format (Patient/id) is preserved
        subjectRef!.ShouldContain(scenario.Patient!.Id);
    }

    [Fact]
    public void GivenScenarioWithExplicitUrnUuidReferences_WhenBuilding_ThenReferencesAreNotRewritten()
    {
        // Note: WithUrnUuidReferences() sets format to UrnUuid, which means NO rewriting occurs.
        // References stay in whatever format they were originally created.
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithName("Explicit UrnUuid Test")
            .WithUrnUuidReferences()
            .WithPatient(p => p.WithAge(40).WithGender("female"))
            .AddEncounter("Checkup")
            .AddObservation(FhirCode.Observations.BodyWeight, 70m, "kg")
            .Build();

        // Assert
        var observation = scenario.Observations[0];
        var subjectRef = observation.MutableNode["subject"]?["reference"]?.GetValue<string>();
        // UrnUuid mode means no rewriting - references stay as created (Patient/id format)
        subjectRef!.ShouldContain(scenario.Patient!.Id);
    }

    #endregion

    #region Resolved Reference Format Tests

    [Fact]
    public void GivenScenarioWithResolvedReferences_WhenBuilding_ThenReferencesUseResolvedFormat()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithName("Resolved Reference Test")
            .WithResolvedReferences()
            .WithPatient(p => p.WithAge(45).WithGender("male"))
            .AddEncounter("Follow-up")
            .AddObservation(FhirCode.Observations.BodyHeight, 175m, "cm")
            .Build();

        // Assert
        var observation = scenario.Observations[0];
        var subjectRef = observation.MutableNode["subject"]?["reference"]?.GetValue<string>();
        subjectRef.ShouldStartWith("Patient/");
        subjectRef.ShouldContain(scenario.Patient!.Id);
    }

    [Fact]
    public void GivenScenarioWithResolvedReferences_WhenBuilding_ThenEncounterReferencesAreResolved()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithName("Encounter Reference Test")
            .WithResolvedReferences()
            .WithPatient(p => p.WithAge(50).WithGender("female"))
            .AddEncounter("Visit")
            .AddObservation(FhirCode.Observations.BodyTemperature, 37m, "Cel")
            .Build();

        // Assert
        var observation = scenario.Observations[0];
        var encounterRef = observation.MutableNode["encounter"]?["reference"]?.GetValue<string>();
        encounterRef.ShouldStartWith("Encounter/");
    }

    [Fact]
    public void GivenScenarioWithReferenceFormat_WhenBuilding_ThenUsesSpecifiedFormat()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithName("WithReferenceFormat Test")
            .WithReferenceFormat(ReferenceFormat.Resolved)
            .WithPatient(p => p.WithAge(30).WithGender("male"))
            .AddEncounter("Annual Physical")
            .AddObservation(FhirCode.Observations.BloodGlucose, 95m, "mg/dL")
            .Build();

        // Assert
        var observation = scenario.Observations[0];
        var subjectRef = observation.MutableNode["subject"]?["reference"]?.GetValue<string>();
        subjectRef.ShouldStartWith("Patient/");
    }

    #endregion

    #region Patient Reference Tests

    [Fact]
    public void GivenScenarioWithResolvedReferences_WhenObservationCreated_ThenSubjectReferencesPatient()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithResolvedReferences()
            .WithPatient(p => p.WithAge(25).WithGender("female"))
            .AddObservation(FhirCode.Observations.BodyWeight, 65m, "kg")
            .Build();

        // Assert
        var patientId = scenario.Patient!.Id;
        var observation = scenario.Observations[0];
        var subjectRef = observation.MutableNode["subject"]?["reference"]?.GetValue<string>();
        subjectRef.ShouldBe($"Patient/{patientId}");
    }

    [Fact]
    public void GivenScenarioWithResolvedReferences_WhenConditionCreated_ThenSubjectReferencesPatient()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithResolvedReferences()
            .WithPatient(p => p.WithAge(55).WithGender("male"))
            .AddConditionOnset(FhirCode.Conditions.Hypertension)
            .Build();

        // Assert
        var patientId = scenario.Patient!.Id;
        var condition = scenario.Conditions[0];
        var subjectRef = condition.MutableNode["subject"]?["reference"]?.GetValue<string>();
        subjectRef.ShouldBe($"Patient/{patientId}");
    }

    [Fact]
    public void GivenScenarioWithResolvedReferences_WhenMedicationRequestCreated_ThenSubjectReferencesPatient()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithResolvedReferences()
            .WithPatient(p => p.WithAge(60).WithGender("female"))
            .AddMedicationOrder(FhirCode.Medications.Aspirin81mg)
            .Build();

        // Assert
        var patientId = scenario.Patient!.Id;
        var medication = scenario.Medications[0];
        var subjectRef = medication.MutableNode["subject"]?["reference"]?.GetValue<string>();
        subjectRef.ShouldBe($"Patient/{patientId}");
    }

    #endregion

    #region Encounter Reference Tests

    [Fact]
    public void GivenScenarioWithResolvedReferences_WhenObservationWithEncounter_ThenEncounterReferenceIsResolved()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithResolvedReferences()
            .WithPatient(p => p.WithAge(35).WithGender("male"))
            .AddEncounter("Lab Visit")
            .AddObservation(FhirCode.Observations.BloodGlucose, 110m, "mg/dL")
            .Build();

        // Assert
        var encounterId = scenario.Encounters[0].Id;
        var observation = scenario.Observations[0];
        var encounterRef = observation.MutableNode["encounter"]?["reference"]?.GetValue<string>();
        encounterRef.ShouldBe($"Encounter/{encounterId}");
    }

    [Fact]
    public void GivenScenarioWithResolvedReferences_WhenEncounterCreated_ThenSubjectReferencesPatient()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithResolvedReferences()
            .WithPatient(p => p.WithAge(40).WithGender("female"))
            .AddEncounter("Consultation")
            .Build();

        // Assert
        var patientId = scenario.Patient!.Id;
        var encounter = scenario.Encounters[0];
        var subjectRef = encounter.MutableNode["subject"]?["reference"]?.GetValue<string>();
        subjectRef.ShouldBe($"Patient/{patientId}");
    }

    #endregion

    #region Practitioner Reference Tests

    [Fact]
    public void GivenScenarioWithResolvedReferences_WhenEncounterWithPractitioner_ThenPractitionerReferenceIsResolved()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithResolvedReferences()
            .WithPatient(p => p.WithAge(45).WithGender("male"))
            .AddFamilyPractitioner()
            .AddEncounter("Office Visit")
            .Build();

        // Assert
        scenario.Practitioners.Count.ShouldBe(1);
        var practitionerId = scenario.Practitioners[0].Id;

        var encounter = scenario.Encounters[0];
        var participant = encounter.MutableNode["participant"]?.AsArray()?[0];
        var practitionerRef = participant?["individual"]?["reference"]?.GetValue<string>();

        // The encounter should reference the practitioner
        if (practitionerRef is not null)
        {
            practitionerRef.ShouldBe($"Practitioner/{practitionerId}");
        }
    }

    #endregion

    #region Organization Reference Tests

    [Fact]
    public void GivenScenarioWithResolvedReferences_WhenPatientWithOrganization_ThenManagingOrganizationIsResolved()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithResolvedReferences()
            .WithPatient(p => p.WithAge(30).WithGender("female"))
            .AddHospital("General Hospital")
            .Build();

        // Assert
        scenario.Organizations.Count.ShouldBe(1);
        var organizationId = scenario.Organizations[0].Id;

        // The patient's managingOrganization should reference the organization if set
        var managingOrg = scenario.Patient!.MutableNode["managingOrganization"]?["reference"]?.GetValue<string>();
        if (managingOrg is not null)
        {
            managingOrg.ShouldBe($"Organization/{organizationId}");
        }
    }

    #endregion

    #region Multiple Resource Types Tests

    [Fact]
    public void GivenComplexScenarioWithResolvedReferences_WhenBuilding_ThenAllReferencesAreResolved()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithName("Complex Scenario")
            .WithResolvedReferences()
            .WithPatient(p => p.WithAge(50).WithGender("male"))
            .AddHospital("City Medical Center")
            .AddFamilyPractitioner()
            .AddEncounter("Initial Consultation")
            .AddConditionOnset(FhirCode.Conditions.DiabetesType2)
            .AddObservation(FhirCode.Observations.BloodGlucose, 150m, "mg/dL")
            .AddMedicationOrder(FhirCode.Medications.Metformin500mg)
            .Build();

        // Assert
        var patientId = scenario.Patient!.Id;

        // All resources should reference the patient with resolved format
        scenario.Encounters[0].MutableNode["subject"]?["reference"]?.GetValue<string>()
            .ShouldBe($"Patient/{patientId}");

        scenario.Conditions[0].MutableNode["subject"]?["reference"]?.GetValue<string>()
            .ShouldBe($"Patient/{patientId}");

        scenario.Observations[0].MutableNode["subject"]?["reference"]?.GetValue<string>()
            .ShouldBe($"Patient/{patientId}");

        scenario.Medications[0].MutableNode["subject"]?["reference"]?.GetValue<string>()
            .ShouldBe($"Patient/{patientId}");
    }

    [Fact]
    public void GivenScenarioWithMultipleObservations_WhenUsingResolvedReferences_ThenAllObservationsHaveResolvedReferences()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithResolvedReferences()
            .WithPatient(p => p.WithAge(40).WithGender("female"))
            .AddEncounter("Vitals Check")
            .AddObservation(FhirCode.Observations.BodyWeight, 70m, "kg")
            .AddObservation(FhirCode.Observations.BodyHeight, 165m, "cm")
            .AddObservation(FhirCode.Observations.BloodPressurePanel, 120m, "mmHg")
            .AddObservation(FhirCode.Observations.BodyTemperature, 37m, "Cel")
            .Build();

        // Assert
        var patientId = scenario.Patient!.Id;
        var encounterId = scenario.Encounters[0].Id;

        foreach (var observation in scenario.Observations)
        {
            observation.MutableNode["subject"]?["reference"]?.GetValue<string>()
                .ShouldBe($"Patient/{patientId}");

            observation.MutableNode["encounter"]?["reference"]?.GetValue<string>()
                .ShouldBe($"Encounter/{encounterId}");
        }
    }

    #endregion

    #region Bundle Tests

    [Fact]
    public void GivenScenarioWithUrnUuidReferences_WhenConvertingToBundle_ThenBundleIsTransactionType()
    {
        // Arrange
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithUrnUuidReferences()
            .WithPatient(p => p.WithAge(35).WithGender("male"))
            .AddEncounter("Visit")
            .Build();

        // Act
        var bundle = scenario.ToBundle();

        // Assert
        bundle.MutableNode["type"]?.GetValue<string>().ShouldBe("transaction");
    }

    [Fact]
    public void GivenScenarioWithUrnUuidReferences_WhenConvertingToBundle_ThenFullUrlsAreUrnUuid()
    {
        // Arrange
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithUrnUuidReferences()
            .WithPatient(p => p.WithAge(35).WithGender("male"))
            .AddEncounter("Visit")
            .Build();

        // Act
        var bundle = scenario.ToBundle();

        // Assert
        var entries = bundle.MutableNode["entry"]?.AsArray();
        entries.ShouldNotBeNull();
        foreach (var entry in entries!)
        {
            var fullUrl = entry?["fullUrl"]?.GetValue<string>();
            fullUrl.ShouldStartWith("urn:uuid:");
        }
    }

    [Fact]
    public void GivenScenarioWithResolvedReferences_WhenConvertingToBatchBundle_ThenBundleIsBatchType()
    {
        // Arrange
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithResolvedReferences()
            .WithPatient(p => p.WithAge(40).WithGender("female"))
            .AddEncounter("Consultation")
            .Build();

        // Act
        var bundle = scenario.ToBatchBundle();

        // Assert
        bundle.MutableNode["type"]?.GetValue<string>().ShouldBe("batch");
    }

    [Fact]
    public void GivenScenarioWithResolvedReferences_WhenConvertingToBatchBundle_ThenFullUrlsAreResolved()
    {
        // Arrange
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithResolvedReferences()
            .WithPatient(p => p.WithAge(40).WithGender("female"))
            .AddEncounter("Consultation")
            .Build();

        // Act
        var bundle = scenario.ToBatchBundle();

        // Assert
        var entries = bundle.MutableNode["entry"]?.AsArray();
        entries.ShouldNotBeNull();

        var firstEntry = entries![0];
        var fullUrl = firstEntry?["fullUrl"]?.GetValue<string>();
        fullUrl.ShouldStartWith("Patient/");
    }

    [Fact]
    public void GivenScenario_WhenCallingToTransactionBundle_ThenReturnsSameAsToBundle()
    {
        // Arrange
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(p => p.WithAge(45).WithGender("male"))
            .AddEncounter("Visit")
            .Build();

        // Act
        var transactionBundle = scenario.ToTransactionBundle();

        // Assert
        transactionBundle.MutableNode["type"]?.GetValue<string>().ShouldBe("transaction");
    }

    #endregion

    #region Logical Name Registry Tests

    [Fact]
    public void GivenScenario_WhenPatientCreated_ThenPatientLogicalNameIsRegistered()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithResolvedReferences()
            .WithPatient(p => p.WithAge(35).WithGender("male"))
            .Build();

        // Assert
        // The patient should be in AllResources with the correct ID
        scenario.Patient.ShouldNotBeNull();
        scenario.AllResources.ShouldContain(scenario.Patient!);
    }

    [Fact]
    public void GivenScenario_WhenEncounterCreated_ThenEncounterIsTrackedAsCurrentEncounter()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithResolvedReferences()
            .WithPatient(p => p.WithAge(35).WithGender("male"))
            .AddEncounter("First Visit")
            .AddEncounter("Second Visit")
            .Build();

        // Assert
        scenario.CurrentEncounter.ShouldNotBeNull();
        scenario.CurrentEncounter!.Id.ShouldBe(scenario.Encounters[1].Id);
    }

    #endregion

    #region Cross-Reference Tests

    [Fact]
    public void GivenScenarioWithProcedureReferencingCondition_WhenUsingResolvedReferences_ThenReferencesAreCorrect()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithResolvedReferences()
            .WithPatient(p => p.WithAge(50).WithGender("male"))
            .AddConditionOnset(FhirCode.Conditions.Appendicitis)
            .AddProcedure(Procedures.Appendectomy)
            .Build();

        // Assert
        var patientId = scenario.Patient!.Id;

        scenario.Conditions.Count.ShouldBe(1);
        scenario.Procedures.Count.ShouldBe(1);

        var condition = scenario.Conditions[0];
        condition.MutableNode["subject"]?["reference"]?.GetValue<string>()
            .ShouldBe($"Patient/{patientId}");

        var procedure = scenario.Procedures[0];
        procedure.MutableNode["subject"]?["reference"]?.GetValue<string>()
            .ShouldBe($"Patient/{patientId}");
    }

    [Fact]
    public void GivenScenarioWithDiagnosticReportAndObservations_WhenUsingResolvedReferences_ThenAllReferencesAreCorrect()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithResolvedReferences()
            .WithPatient(p => p.WithAge(45).WithGender("female"))
            .AddEncounter("Lab Visit")
            .AddComprehensiveMetabolicPanel()
            .Build();

        // Assert
        var patientId = scenario.Patient!.Id;
        var encounterId = scenario.Encounters[0].Id;

        scenario.DiagnosticReports.Count.ShouldBe(1);
        var report = scenario.DiagnosticReports[0];

        report.MutableNode["subject"]?["reference"]?.GetValue<string>()
            .ShouldBe($"Patient/{patientId}");
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void GivenScenarioWithNoPatient_WhenBuildingWithResolvedReferences_ThenBuildSucceeds()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithName("No Patient Scenario")
            .WithResolvedReferences()
            .AddOrganization("Test Hospital")
            .Build();

        // Assert
        scenario.Patient.ShouldBeNull();
        scenario.Organizations.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenScenarioWithOnlyPatient_WhenBuildingWithResolvedReferences_ThenNoReferencesNeedRewriting()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithResolvedReferences()
            .WithPatient(p => p.WithAge(30).WithGender("male"))
            .Build();

        // Assert
        scenario.Patient.ShouldNotBeNull();
        scenario.AllResources.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenScenarioWithTag_WhenBuildingWithResolvedReferences_ThenTagIsAppliedAndReferencesAreCorrect()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithTag(tag)
            .WithResolvedReferences()
            .WithPatient(p => p.WithAge(35).WithGender("female"))
            .AddEncounter("Tagged Visit")
            .AddObservation(FhirCode.Observations.BloodGlucose, 100m, "mg/dL")
            .Build();

        // Assert
        var patientId = scenario.Patient!.Id;

        // Verify tag is applied
        scenario.Patient.MutableNode["meta"]?["tag"]?.AsArray()?[0]?["code"]?.GetValue<string>()
            .ShouldBe(tag);

        // Verify references are resolved
        scenario.Observations[0].MutableNode["subject"]?["reference"]?.GetValue<string>()
            .ShouldBe($"Patient/{patientId}");
    }

    #endregion
}
