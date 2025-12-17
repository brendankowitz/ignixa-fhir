// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
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
        scenario.DiagnosticReports.Count.ShouldBe(1);
        var report = scenario.DiagnosticReports[0];
        report.ResourceType.ShouldBe("DiagnosticReport");
        report.Id.ShouldNotBeNullOrEmpty();
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
        status.ShouldBe("final");
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
        categoryCode.ShouldBe("LAB");
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
        code.ShouldBe("24323-8"); // LOINC code for CMP
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
        subjectRef.ShouldBe($"urn:uuid:{scenario.Patient!.Id}");
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
        encounterRef.ShouldBe($"urn:uuid:{scenario.Encounters[0].Id}");
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
        scenario.Observations.Count.ShouldBe(14);
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
        results.ShouldNotBeNull();
        results!.Count.ShouldBe(14);
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

        glucoseObs.ShouldNotBeNull();
        var value = glucoseObs!.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>();
        value.ShouldBe(95); // Default value in CMP factory method
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
        scenario.Observations.Count.ShouldBe(2);

        var glucoseObs = scenario.Observations.FirstOrDefault(o =>
        {
            var code = o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "2339-0";
        });

        glucoseObs.ShouldNotBeNull();
        var value = glucoseObs!.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>();
        value.ShouldBe(150);
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
        categoryCode.ShouldBe("RAD");
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
        reportConclusion.ShouldBe(conclusion);
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
        scenario.Observations.ShouldBeEmpty();
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
        scenario.Observations.Count.ShouldBe(8);
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
        scenario.Observations.Count.ShouldBe(4);
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
        scenario.DiagnosticReports.Count.ShouldBe(3);
        scenario.Observations.Count.ShouldBe(14 + 8 + 4); // CMP + CBC + Lipid
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
        reportEvents.Count.ShouldBe(1);
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
        scenario.AllResources.ShouldContain(scenario.DiagnosticReports[0]);
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
        encounterRef.ShouldBeNull();
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
        // DiagnosticReport.performer is a BackboneElement[] with actor (Reference)
        // The actor reference can have either "reference" or "display" or both
        var report = scenario.DiagnosticReports[0];
        var performerActor = report.MutableNode["performer"]?[0]?["actor"];
        performerActor.ShouldNotBeNull();
        var hasRefOrDisplay = performerActor?["reference"] != null || performerActor?["display"] != null;
        hasRefOrDisplay.ShouldBeTrue("performer.actor should have either reference or display");
    }

    #endregion
}
