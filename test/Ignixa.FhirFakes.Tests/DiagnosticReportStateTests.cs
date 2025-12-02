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
/// Tests for DiagnosticReportState. Tests lab panels and imaging reports generation.
/// Uses AAA pattern and BDD naming convention.
/// </summary>
public class DiagnosticReportStateTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();

    #region Basic Generation Tests

    [Fact]
    public void GivenLabPanel_WhenGenerated_ThenCreatesDiagnosticReport()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Lab work")
            .AddComprehensiveMetabolicPanel()
            .Build();

        // Assert
        scenario.DiagnosticReports.Should().HaveCount(1);
        var report = scenario.DiagnosticReports[0];
        report.ResourceType.Should().Be("DiagnosticReport");
        report.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GivenLabPanel_WhenGenerated_ThenHasCorrectStatus()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Lab work")
            .AddComprehensiveMetabolicPanel()
            .Build();

        // Assert
        var report = scenario.DiagnosticReports[0];
        var status = report.MutableNode["status"]?.GetValue<string>();
        status.Should().Be("final");
    }

    [Fact]
    public void GivenLabPanel_WhenGenerated_ThenHasLaboratoryCategory()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Lab work")
            .AddComprehensiveMetabolicPanel()
            .Build();

        // Assert
        var report = scenario.DiagnosticReports[0];
        var categoryCode = report.MutableNode["category"]?[0]?["coding"]?[0]?["code"]?.GetValue<string>();
        categoryCode.Should().Be("LAB");
    }

    [Fact]
    public void GivenLabPanel_WhenGenerated_ThenHasCorrectCode()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Lab work")
            .AddComprehensiveMetabolicPanel()
            .Build();

        // Assert
        var report = scenario.DiagnosticReports[0];
        var code = report.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
        code.Should().Be("24323-8"); // LOINC code for CMP
    }

    #endregion

    #region Patient Reference Tests

    [Fact]
    public void GivenDiagnosticReport_WhenGenerated_ThenReferencesPatient()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Lab work")
            .AddComprehensiveMetabolicPanel()
            .Build();

        // Assert
        var report = scenario.DiagnosticReports[0];
        var subjectRef = report.MutableNode["subject"]?["reference"]?.GetValue<string>();
        subjectRef.Should().Be($"Patient/{scenario.Patient!.Id}");
    }

    [Fact]
    public void GivenDiagnosticReport_WhenEncounterExists_ThenReferencesEncounter()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Lab work")
            .AddComprehensiveMetabolicPanel()
            .Build();

        // Assert
        var report = scenario.DiagnosticReports[0];
        var encounterRef = report.MutableNode["encounter"]?["reference"]?.GetValue<string>();
        encounterRef.Should().Be($"Encounter/{scenario.Encounters[0].Id}");
    }

    #endregion

    #region Observation Generation Tests

    [Fact]
    public void GivenLabPanelWithObservations_WhenGenerated_ThenCreatesObservations()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Lab work")
            .AddComprehensiveMetabolicPanel()
            .Build();

        // Assert - CMP should have 14 observations
        scenario.Observations.Should().HaveCount(14);
    }

    [Fact]
    public void GivenLabPanelWithObservations_WhenGenerated_ThenReportLinksToObservations()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Lab work")
            .AddComprehensiveMetabolicPanel()
            .Build();

        // Assert
        var report = scenario.DiagnosticReports[0];
        var results = report.MutableNode["result"] as System.Text.Json.Nodes.JsonArray;
        results.Should().NotBeNull();
        results!.Count.Should().Be(14);
    }

    [Fact]
    public void GivenLabPanelWithObservations_WhenGenerated_ThenObservationsHaveCorrectValues()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Lab work")
            .AddComprehensiveMetabolicPanel()
            .Build();

        // Assert - Find glucose observation
        var glucoseObs = scenario.Observations.FirstOrDefault(o =>
        {
            var code = o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "2339-0"; // LOINC code for glucose
        });

        glucoseObs.Should().NotBeNull();
        var value = glucoseObs!.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>();
        value.Should().Be(95); // Default value in CMP factory method
    }

    [Fact]
    public void GivenCustomObservations_WhenGenerated_ThenUsesProvidedValues()
    {
        // Arrange & Act
        var observations = new (FhirCode Code, decimal Value, string Unit)[]
        {
            (LabObservations.Glucose, 150, "mg/dL"),
            (LabObservations.Sodium, 138, "mmol/L")
        };

        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Lab work")
            .AddDiagnosticReport(DiagnosticReports.BasicMetabolicPanel, observations)
            .Build();

        // Assert
        scenario.Observations.Should().HaveCount(2);

        var glucoseObs = scenario.Observations.FirstOrDefault(o =>
        {
            var code = o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "2339-0";
        });

        glucoseObs.Should().NotBeNull();
        var value = glucoseObs!.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>();
        value.Should().Be(150);
    }

    #endregion

    #region Imaging Report Tests

    [Fact]
    public void GivenImagingReport_WhenGenerated_ThenHasRadiologyCategory()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Imaging")
            .AddChestXRay()
            .Build();

        // Assert
        var report = scenario.DiagnosticReports[0];
        var categoryCode = report.MutableNode["category"]?[0]?["coding"]?[0]?["code"]?.GetValue<string>();
        categoryCode.Should().Be("RAD");
    }

    [Fact]
    public void GivenImagingReport_WhenGenerated_ThenHasConclusion()
    {
        // Arrange & Act
        var conclusion = "Clear lungs, no masses identified.";
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Imaging")
            .AddChestXRay(conclusion)
            .Build();

        // Assert
        var report = scenario.DiagnosticReports[0];
        var reportConclusion = report.MutableNode["conclusion"]?.GetValue<string>();
        reportConclusion.Should().Be(conclusion);
    }

    [Fact]
    public void GivenImagingReport_WhenGenerated_ThenHasNoObservations()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Imaging")
            .AddChestXRay()
            .Build();

        // Assert
        scenario.Observations.Should().BeEmpty();
    }

    #endregion

    #region Factory Method Tests

    [Fact]
    public void GivenCBCFactory_WhenGenerated_ThenHasCorrectObservationCount()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Lab work")
            .AddCompleteBloodCount()
            .Build();

        // Assert - CBC has 8 observations
        scenario.Observations.Should().HaveCount(8);
    }

    [Fact]
    public void GivenLipidPanelFactory_WhenGenerated_ThenHasCorrectObservationCount()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Lab work")
            .AddLipidPanel()
            .Build();

        // Assert - Lipid panel has 4 observations
        scenario.Observations.Should().HaveCount(4);
    }

    #endregion

    #region Timeline and Context Tests

    [Fact]
    public void GivenMultipleDiagnosticReports_WhenGenerated_ThenAllAddedToContext()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Annual checkup")
            .AddComprehensiveMetabolicPanel()
            .AddCompleteBloodCount()
            .AddLipidPanel()
            .Build();

        // Assert
        scenario.DiagnosticReports.Should().HaveCount(3);
        scenario.Observations.Should().HaveCount(14 + 8 + 4); // CMP + CBC + Lipid
    }

    [Fact]
    public void GivenDiagnosticReport_WhenGenerated_ThenAppearsInTimeline()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Lab work")
            .AddComprehensiveMetabolicPanel()
            .Build();

        // Assert
        var reportEvents = scenario.Timeline.Where(e => e.EventType == "DiagnosticReport").ToList();
        reportEvents.Should().HaveCount(1);
    }

    [Fact]
    public void GivenDiagnosticReport_WhenGenerated_ThenAppearsInAllResources()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Lab work")
            .AddComprehensiveMetabolicPanel()
            .Build();

        // Assert
        scenario.AllResources.Should().Contain(scenario.DiagnosticReports[0]);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void GivenDiagnosticReportWithoutEncounter_WhenGenerated_ThenCreatesWithoutEncounterReference()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddDiagnosticReport(DiagnosticReportState.ComprehensiveMetabolicPanel())
            .Build();

        // Assert
        var report = scenario.DiagnosticReports[0];
        var encounterRef = report.MutableNode["encounter"];
        encounterRef.Should().BeNull();
    }

    [Fact]
    public void GivenDiagnosticReportWithPerformer_WhenGenerated_ThenHasPerformer()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Lab work")
            .AddComprehensiveMetabolicPanel()
            .Build();

        // Assert
        var report = scenario.DiagnosticReports[0];
        var performer = report.MutableNode["performer"]?[0]?["display"]?.GetValue<string>();
        performer.Should().NotBeNullOrEmpty();
    }

    #endregion
}
