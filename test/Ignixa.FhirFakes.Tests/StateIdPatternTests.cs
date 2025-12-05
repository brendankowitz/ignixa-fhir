// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFakes.Tests;

/// <summary>
/// Tests for StateId pattern and cross-state references.
/// Verifies StateId registration, retrieval, and usage in DiagnosticReports and CareTeams.
/// Uses AAA pattern and BDD naming convention.
/// </summary>
public class StateIdPatternTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();

    #region StateId Registration Tests

    [Fact]
    public void GivenObservationWithStateId_WhenBuilt_ThenCanRetrieveByStateId()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddState(new ObservationState
            {
                StateId = "obs_glucose",
                Code = LabObservations.Glucose,
                Value = 105m,
                Unit = "mg/dL"
            })
            .Build();

        // Assert
        var observation = scenario.GetStateResource("obs_glucose");
        observation.Should().NotBeNull();
        observation!.ResourceType.Should().Be("Observation");
        observation.Id.Should().Be(scenario.Observations[0].Id);
    }

    [Fact]
    public void GivenObservationWithoutStateId_WhenBuilt_ThenNotRetrievable()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddObservation(LabObservations.Glucose, 105m, "mg/dL")
            .Build();

        // Assert
        var observation = scenario.GetStateResource("any_id");
        observation.Should().BeNull();
    }

    [Fact]
    public void GivenMultipleStatesWithStateIds_WhenBuilt_ThenAllRetrievable()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddState(new ObservationState
            {
                StateId = "obs1",
                Code = LabObservations.Glucose,
                Value = 105m,
                Unit = "mg/dL"
            })
            .AddState(new ObservationState
            {
                StateId = "obs2",
                Code = LabObservations.TotalCholesterol,
                Value = 180m,
                Unit = "mg/dL"
            })
            .AddState(new ConditionOnsetState
            {
                StateId = "cond1",
                Code = FhirCode.Conditions.DiabetesType2,
                Severity = 2
            })
            .Build();

        // Assert
        scenario.GetStateResource("obs1").Should().NotBeNull();
        scenario.GetStateResource("obs2").Should().NotBeNull();
        scenario.GetStateResource("cond1").Should().NotBeNull();
    }

    #endregion

    #region DiagnosticReport Referenced Observations Tests

    [Fact]
    public void GivenDiagnosticReportWithReferencedObservations_WhenBuilt_ThenReferencesExistingObservations()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Lab visit")
            .AddState(new ObservationState
            {
                StateId = "obs_glucose",
                Code = LabObservations.Glucose,
                Value = 105m,
                Unit = "mg/dL"
            })
            .AddDiagnosticReport(new DiagnosticReportState
            {
                Code = DiagnosticReports.BasicMetabolicPanel,
                ReferencedObservationStateIds = ["obs_glucose"]
            })
            .Build();

        // Assert
        scenario.Observations.Should().HaveCount(1, "should not create duplicate observations");
        var report = scenario.DiagnosticReports[0];
        var resultRef = report.MutableNode["result"]![0]!["reference"]!.GetValue<string>();
        resultRef.Should().Be($"Observation/{scenario.Observations[0].Id}");
    }

    [Fact]
    public void GivenDiagnosticReportWithMultipleReferencedObservations_WhenBuilt_ThenReferencesAllObservations()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Lab visit")
            .AddState(new ObservationState
            {
                StateId = "obs_glucose",
                Code = LabObservations.Glucose,
                Value = 105m,
                Unit = "mg/dL"
            })
            .AddState(new ObservationState
            {
                StateId = "obs_a1c",
                Code = LabObservations.HemoglobinA1c,
                Value = 7.2m,
                Unit = "%"
            })
            .AddDiagnosticReport(new DiagnosticReportState
            {
                Code = DiagnosticReports.BasicMetabolicPanel,
                ReferencedObservationStateIds = ["obs_glucose", "obs_a1c"]
            })
            .Build();

        // Assert
        scenario.Observations.Should().HaveCount(2);
        var report = scenario.DiagnosticReports[0];
        var results = report.MutableNode["result"]!.AsArray();
        results.Should().HaveCount(2);
        results[0]!["reference"]!.GetValue<string>().Should().Contain(scenario.Observations[0].Id);
        results[1]!["reference"]!.GetValue<string>().Should().Contain(scenario.Observations[1].Id);
    }

    [Fact]
    public void GivenDiagnosticReportWithMixedObservations_WhenBuilt_ThenHandlesBothReferencedAndInline()
    {
        // Arrange & Act
        var glucoseCode = LabObservations.Glucose;
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Lab visit")
            .AddState(new ObservationState
            {
                StateId = "obs_glucose",
                Code = glucoseCode,
                Value = 105m,
                Unit = "mg/dL"
            })
            .AddDiagnosticReport(new DiagnosticReportState
            {
                Code = DiagnosticReports.BasicMetabolicPanel,
                ReferencedObservationStateIds = ["obs_glucose"],
                Observations = [(LabObservations.Sodium, 140m, "mmol/L")]
            })
            .Build();

        // Assert
        scenario.Observations.Should().HaveCount(2, "one referenced + one inline");
        var report = scenario.DiagnosticReports[0];
        var results = report.MutableNode["result"]!.AsArray();
        results.Should().HaveCount(2);
    }

    [Fact]
    public void GivenDiagnosticReportWithNonExistentStateId_WhenBuilt_ThenSkipsInvalidReference()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Lab visit")
            .AddDiagnosticReport(new DiagnosticReportState
            {
                Code = DiagnosticReports.BasicMetabolicPanel,
                ReferencedObservationStateIds = ["non_existent_id"]
            })
            .Build();

        // Assert
        scenario.Observations.Should().HaveCount(0, "no observations created");
        var report = scenario.DiagnosticReports[0];
        var results = report.MutableNode["result"]?.AsArray();

        // The result field might be null or an empty array depending on implementation
        if (results is not null)
        {
            results.Should().BeEmpty("should not add invalid references");
        }
    }

    #endregion

    #region Organization CustomIdentifiers Tests

    [Fact]
    public void GivenOrganizationWithCustomIdentifier_WhenBuilt_ThenHasCustomIdentifier()
    {
        // Arrange
        var customId = Guid.NewGuid().ToString();

        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddOrganization(new OrganizationState
            {
                OrganizationName = "Test Clinic",
                CustomIdentifiers = [("http://test-system", customId)]
            })
            .Build();

        // Assert
        var org = scenario.Organizations[0];
        var identifiers = org.MutableNode["identifier"]!.AsArray();
        identifiers.Should().Contain(i =>
            i!["system"]!.GetValue<string>() == "http://test-system" &&
            i["value"]!.GetValue<string>() == customId);
    }

    [Fact]
    public void GivenOrganizationWithMultipleCustomIdentifiers_WhenBuilt_ThenHasAllIdentifiers()
    {
        // Arrange
        var id1 = Guid.NewGuid().ToString();
        var id2 = Guid.NewGuid().ToString();

        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddOrganization(new OrganizationState
            {
                OrganizationName = "Test Clinic",
                CustomIdentifiers = [
                    ("http://system1", id1),
                    ("http://system2", id2)
                ]
            })
            .Build();

        // Assert
        var org = scenario.Organizations[0];
        var identifiers = org.MutableNode["identifier"]!.AsArray();
        identifiers.Should().HaveCountGreaterOrEqualTo(4, "NPI + TaxId + 2 custom");
        identifiers.Should().Contain(i => i!["value"]!.GetValue<string>() == id1);
        identifiers.Should().Contain(i => i!["value"]!.GetValue<string>() == id2);
    }

    [Fact]
    public void GivenOrganizationWithoutCustomIdentifiers_WhenBuilt_ThenHasOnlyStandardIdentifiers()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddOrganization(new OrganizationState
            {
                OrganizationName = "Standard Clinic"
            })
            .Build();

        // Assert
        var org = scenario.Organizations[0];
        var identifiers = org.MutableNode["identifier"]!.AsArray();
        identifiers.Should().HaveCount(2, "NPI + TaxId only");

        var npiIdentifier = identifiers.FirstOrDefault(i =>
            i!["system"]!.GetValue<string>() == OrganizationState.NpiSystem);
        var taxIdIdentifier = identifiers.FirstOrDefault(i =>
            i!["system"]!.GetValue<string>() == OrganizationState.TaxIdSystem);

        npiIdentifier.Should().NotBeNull();
        taxIdIdentifier.Should().NotBeNull();
    }

    #endregion

    #region CareTeam Tests

    [Fact]
    public void GivenCareTeam_WhenBuilt_ThenLinksToPatient()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(familyName: "TestPatient")
            .AddCareTeam("Cardiac Care Team")
            .Build();

        // Assert
        scenario.CareTeams.Should().HaveCount(1);
        var careTeam = scenario.CareTeams[0];
        careTeam.MutableNode["subject"]!["reference"]!.GetValue<string>()
            .Should().Contain(scenario.Patient!.Id);
    }

    [Fact]
    public void GivenCareTeamWithPractitioners_WhenBuilt_ThenIncludesParticipants()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddState(new PractitionerState
            {
                StateId = "dr_smith",
                Specialty = Specialties.FamilyMedicine,
                FamilyName = "Smith"
            })
            .AddState(new PractitionerState
            {
                StateId = "dr_jones",
                Specialty = Specialties.Cardiology,
                FamilyName = "Jones"
            })
            .AddCareTeam(new CareTeamState
            {
                TeamName = "Multi-specialty Team",
                ParticipantStateIds = ["dr_smith", "dr_jones"]
            })
            .Build();

        // Assert
        var careTeam = scenario.CareTeams[0];
        var participants = careTeam.MutableNode["participant"]!.AsArray();
        participants.Should().HaveCount(2);
        participants[0]!["member"]!["reference"]!.GetValue<string>()
            .Should().Contain(scenario.Practitioners[0].Id);
        participants[1]!["member"]!["reference"]!.GetValue<string>()
            .Should().Contain(scenario.Practitioners[1].Id);
    }

    [Fact]
    public void GivenCareTeamWithoutPractitioners_WhenBuilt_ThenHasNoParticipants()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCareTeam("Empty Team")
            .Build();

        // Assert
        var careTeam = scenario.CareTeams[0];
        var participants = careTeam.MutableNode["participant"];
        participants.Should().BeNull("no participants specified");
    }

    [Fact]
    public void GivenCareTeamWithNonExistentPractitioner_WhenBuilt_ThenSkipsInvalidReference()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCareTeam(new CareTeamState
            {
                TeamName = "Invalid Team",
                ParticipantStateIds = ["non_existent_practitioner"]
            })
            .Build();

        // Assert
        var careTeam = scenario.CareTeams[0];
        var participants = careTeam.MutableNode["participant"]?.AsArray();
        participants.Should().NotBeNull();
        participants!.Should().BeEmpty("should not add invalid references");
    }

    [Fact]
    public void GivenCareTeamWithMixedValidAndInvalidPractitioners_WhenBuilt_ThenIncludesOnlyValid()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddState(new PractitionerState
            {
                StateId = "dr_smith",
                Specialty = Specialties.FamilyMedicine,
                FamilyName = "Smith"
            })
            .AddCareTeam(new CareTeamState
            {
                TeamName = "Partial Team",
                ParticipantStateIds = ["dr_smith", "non_existent_dr", "also_fake"]
            })
            .Build();

        // Assert
        var careTeam = scenario.CareTeams[0];
        var participants = careTeam.MutableNode["participant"]!.AsArray();
        participants.Should().HaveCount(1, "only valid practitioner should be added");
        participants[0]!["member"]!["reference"]!.GetValue<string>()
            .Should().Contain(scenario.Practitioners[0].Id);
    }

    #endregion

    #region Cross-State Reference Integration Tests

    [Fact]
    public void GivenComplexScenarioWithMultipleStateIds_WhenBuilt_ThenAllReferencesResolved()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Annual checkup")
            .AddState(new PractitionerState
            {
                StateId = "dr_primary",
                Specialty = Specialties.FamilyMedicine,
                FamilyName = "Primary"
            })
            .AddState(new ObservationState
            {
                StateId = "obs_bp_systolic",
                Code = FhirCode.Observations.BloodPressureSystolic,
                Value = 120m,
                Unit = "mmHg"
            })
            .AddState(new ObservationState
            {
                StateId = "obs_bp_diastolic",
                Code = FhirCode.Observations.BloodPressureDiastolic,
                Value = 80m,
                Unit = "mmHg"
            })
            .AddDiagnosticReport(new DiagnosticReportState
            {
                Code = DiagnosticReports.BasicMetabolicPanel,
                ReferencedObservationStateIds = ["obs_bp_systolic", "obs_bp_diastolic"]
            })
            .AddCareTeam(new CareTeamState
            {
                TeamName = "Primary Care Team",
                ParticipantStateIds = ["dr_primary"]
            })
            .Build();

        // Assert
        scenario.Observations.Should().HaveCount(2, "two observations created");
        scenario.DiagnosticReports.Should().HaveCount(1);
        scenario.CareTeams.Should().HaveCount(1);

        var report = scenario.DiagnosticReports[0];
        var reportResults = report.MutableNode["result"]!.AsArray();
        reportResults.Should().HaveCount(2, "references both observations");

        var careTeam = scenario.CareTeams[0];
        var careTeamParticipants = careTeam.MutableNode["participant"]!.AsArray();
        careTeamParticipants.Should().HaveCount(1, "references the practitioner");
    }

    [Fact]
    public void GivenStateIdReusedAcrossTypes_WhenBuilt_ThenEachStateIdIsUnique()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddState(new ObservationState
            {
                StateId = "unique1",
                Code = LabObservations.Glucose,
                Value = 105m,
                Unit = "mg/dL"
            })
            .AddState(new PractitionerState
            {
                StateId = "unique2",
                Specialty = Specialties.FamilyMedicine,
                FamilyName = "Test"
            })
            .Build();

        // Assert
        var obs = scenario.GetStateResource("unique1");
        var pract = scenario.GetStateResource("unique2");

        obs.Should().NotBeNull();
        obs!.ResourceType.Should().Be("Observation");

        pract.Should().NotBeNull();
        pract!.ResourceType.Should().Be("Practitioner");

        obs.Id.Should().NotBe(pract.Id, "different resources have different IDs");
    }

    [Fact]
    public void GivenOrganizationWithStateId_WhenRetrieved_ThenReturnsOrganization()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddState(new OrganizationState
            {
                StateId = "org_main",
                OrganizationName = "Main Hospital"
            })
            .Build();

        // Assert
        var org = scenario.GetStateResource("org_main");
        org.Should().NotBeNull();
        org!.ResourceType.Should().Be("Organization");
        org.MutableNode["name"]!.GetValue<string>().Should().Be("Main Hospital");
    }

    #endregion
}
