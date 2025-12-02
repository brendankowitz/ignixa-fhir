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
/// Tests for ProcedureState. Tests surgeries and diagnostic procedures generation.
/// Uses AAA pattern and BDD naming convention.
/// </summary>
public class ProcedureStateTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();

    #region Basic Generation Tests

    [Fact]
    public void GivenProcedure_WhenGenerated_ThenCreatesProcedure()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Surgery")
            .AddAppendectomy()
            .Build();

        // Assert
        scenario.Procedures.Should().HaveCount(1);
        var procedure = scenario.Procedures[0];
        procedure.ResourceType.Should().Be("Procedure");
        procedure.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GivenProcedure_WhenGenerated_ThenHasCompletedStatus()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Surgery")
            .AddAppendectomy()
            .Build();

        // Assert
        var procedure = scenario.Procedures[0];
        var status = procedure.MutableNode["status"]?.GetValue<string>();
        status.Should().Be("completed");
    }

    [Fact]
    public void GivenProcedure_WhenGenerated_ThenHasCorrectCode()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Surgery")
            .AddAppendectomy()
            .Build();

        // Assert
        var procedure = scenario.Procedures[0];
        var code = procedure.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
        code.Should().Be("80146002"); // SNOMED CT for appendectomy
    }

    #endregion

    #region Patient Reference Tests

    [Fact]
    public void GivenProcedure_WhenGenerated_ThenReferencesPatient()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Surgery")
            .AddAppendectomy()
            .Build();

        // Assert
        var procedure = scenario.Procedures[0];
        var subjectRef = procedure.MutableNode["subject"]?["reference"]?.GetValue<string>();
        subjectRef.Should().Be($"Patient/{scenario.Patient!.Id}");
    }

    [Fact]
    public void GivenProcedure_WhenEncounterExists_ThenReferencesEncounter()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Surgery")
            .AddAppendectomy()
            .Build();

        // Assert
        var procedure = scenario.Procedures[0];
        var encounterRef = procedure.MutableNode["encounter"]?["reference"]?.GetValue<string>();
        encounterRef.Should().Be($"Encounter/{scenario.Encounters[0].Id}");
    }

    #endregion

    #region Duration Tests

    [Fact]
    public void GivenProcedureWithDuration_WhenGenerated_ThenHasPerformedPeriod()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Surgery")
            .AddAppendectomy() // Has 90 minute duration
            .Build();

        // Assert
        var procedure = scenario.Procedures[0];
        var start = procedure.MutableNode["performedPeriod"]?["start"]?.GetValue<string>();
        var end = procedure.MutableNode["performedPeriod"]?["end"]?.GetValue<string>();

        start.Should().NotBeNullOrEmpty();
        end.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GivenProcedureWithDuration_WhenGenerated_ThenEndIsAfterStart()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Surgery")
            .AddAppendectomy()
            .Build();

        // Assert
        var procedure = scenario.Procedures[0];
        var start = DateTime.Parse(procedure.MutableNode["performedPeriod"]!["start"]!.GetValue<string>()!);
        var end = DateTime.Parse(procedure.MutableNode["performedPeriod"]!["end"]!.GetValue<string>()!);

        (end - start).TotalMinutes.Should().BeApproximately(90, 1);
    }

    [Fact]
    public void GivenCustomDuration_WhenGenerated_ThenUsesProvidedDuration()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Surgery")
            .AddProcedure(Procedures.Colonoscopy, duration: TimeSpan.FromMinutes(30))
            .Build();

        // Assert
        var procedure = scenario.Procedures[0];
        var start = DateTime.Parse(procedure.MutableNode["performedPeriod"]!["start"]!.GetValue<string>()!);
        var end = DateTime.Parse(procedure.MutableNode["performedPeriod"]!["end"]!.GetValue<string>()!);

        (end - start).TotalMinutes.Should().BeApproximately(30, 1);
    }

    #endregion

    #region Category Tests

    [Fact]
    public void GivenSurgicalProcedure_WhenGenerated_ThenHasSurgicalCategory()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Surgery")
            .AddAppendectomy()
            .Build();

        // Assert
        var procedure = scenario.Procedures[0];
        var categoryCode = procedure.MutableNode["category"]?["coding"]?[0]?["code"]?.GetValue<string>();
        categoryCode.Should().Be("387713003"); // SNOMED CT for surgical procedure
    }

    [Fact]
    public void GivenDiagnosticProcedure_WhenGenerated_ThenHasDiagnosticCategory()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Procedure")
            .AddColonoscopy()
            .Build();

        // Assert
        var procedure = scenario.Procedures[0];
        var categoryCode = procedure.MutableNode["category"]?["coding"]?[0]?["code"]?.GetValue<string>();
        categoryCode.Should().Be("103693007"); // SNOMED CT for diagnostic procedure
    }

    #endregion

    #region Body Site Tests

    [Fact]
    public void GivenProcedureWithBodySite_WhenGenerated_ThenHasBodySite()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Surgery")
            .AddAppendectomy() // Has body site = Appendix
            .Build();

        // Assert
        var procedure = scenario.Procedures[0];
        var bodySite = procedure.MutableNode["bodySite"]?[0]?["text"]?.GetValue<string>();
        bodySite.Should().Be("Appendix");
    }

    [Fact]
    public void GivenProcedureWithBodySite_WhenGenerated_ThenHasCorrectBodySiteCode()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Surgery")
            .AddAppendectomy()
            .Build();

        // Assert
        var procedure = scenario.Procedures[0];
        var bodySiteCode = procedure.MutableNode["bodySite"]?[0]?["coding"]?[0]?["code"]?.GetValue<string>();
        bodySiteCode.Should().Be("66754008"); // SNOMED CT for appendix
    }

    [Fact]
    public void GivenCustomBodySite_WhenGenerated_ThenUsesProvidedBodySite()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Surgery")
            .AddProcedure(Procedures.Biopsy, bodySite: "left arm")
            .Build();

        // Assert
        var procedure = scenario.Procedures[0];
        var bodySite = procedure.MutableNode["bodySite"]?[0]?["text"]?.GetValue<string>();
        bodySite.Should().Be("left arm");
    }

    #endregion

    #region Outcome Tests

    [Fact]
    public void GivenProcedureWithOutcome_WhenGenerated_ThenHasOutcome()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Surgery")
            .AddAppendectomy()
            .Build();

        // Assert
        var procedure = scenario.Procedures[0];
        var outcome = procedure.MutableNode["outcome"]?["text"]?.GetValue<string>();
        outcome.Should().Contain("without complications");
    }

    [Fact]
    public void GivenCustomOutcome_WhenGenerated_ThenUsesProvidedOutcome()
    {
        // Arrange & Act
        var customOutcome = "Procedure completed successfully with minimal blood loss";
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Surgery")
            .AddProcedure(Procedures.Cholecystectomy, outcome: customOutcome)
            .Build();

        // Assert
        var procedure = scenario.Procedures[0];
        var outcome = procedure.MutableNode["outcome"]?["text"]?.GetValue<string>();
        outcome.Should().Be(customOutcome);
    }

    #endregion

    #region Reason Tests

    [Fact]
    public void GivenProcedureWithReason_WhenGenerated_ThenHasReasonCode()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Surgery")
            .AddProcedure(Procedures.CABG, reason: "Coronary artery disease")
            .Build();

        // Assert
        var procedure = scenario.Procedures[0];
        var reason = procedure.MutableNode["reasonCode"]?[0]?["text"]?.GetValue<string>();
        reason.Should().Be("Coronary artery disease");
    }

    #endregion

    #region Factory Method Tests

    [Fact]
    public void GivenColonoscopyFactory_WhenGenerated_ThenHasCorrectDuration()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Procedure")
            .AddColonoscopy()
            .Build();

        // Assert
        var procedure = scenario.Procedures[0];
        var start = DateTime.Parse(procedure.MutableNode["performedPeriod"]!["start"]!.GetValue<string>()!);
        var end = DateTime.Parse(procedure.MutableNode["performedPeriod"]!["end"]!.GetValue<string>()!);

        (end - start).TotalMinutes.Should().BeApproximately(45, 1);
    }

    [Fact]
    public void GivenColonoscopyFactory_WhenGenerated_ThenHasFollowUp()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Procedure")
            .AddColonoscopy()
            .Build();

        // Assert
        var procedure = scenario.Procedures[0];
        var followUp = procedure.MutableNode["followUp"]?[0]?["text"]?.GetValue<string>();
        followUp.Should().Contain("Repeat colonoscopy");
    }

    [Fact]
    public void GivenCustomColonoscopyOutcome_WhenGenerated_ThenUsesProvidedOutcome()
    {
        // Arrange & Act
        var customOutcome = "3 polyps found and removed";
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Procedure")
            .AddColonoscopy(customOutcome)
            .Build();

        // Assert
        var procedure = scenario.Procedures[0];
        var outcome = procedure.MutableNode["outcome"]?["text"]?.GetValue<string>();
        outcome.Should().Be(customOutcome);
    }

    [Fact]
    public void GivenCABGFactory_WhenGenerated_ThenHasCorrectDuration()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Surgery")
            .AddProcedure(ProcedureState.CABG())
            .Build();

        // Assert
        var procedure = scenario.Procedures[0];
        var start = DateTime.Parse(procedure.MutableNode["performedPeriod"]!["start"]!.GetValue<string>()!);
        var end = DateTime.Parse(procedure.MutableNode["performedPeriod"]!["end"]!.GetValue<string>()!);

        (end - start).TotalHours.Should().BeApproximately(4, 0.1);
    }

    #endregion

    #region Performer Tests

    [Fact]
    public void GivenProcedure_WhenGenerated_ThenHasPerformer()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Surgery")
            .AddAppendectomy()
            .Build();

        // Assert
        var procedure = scenario.Procedures[0];
        var performer = procedure.MutableNode["performer"]?[0]?["actor"]?["display"]?.GetValue<string>();
        performer.Should().NotBeNullOrEmpty();
        performer.Should().Contain("Dr.");
    }

    [Fact]
    public void GivenProcedure_WhenGenerated_ThenHasLocation()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Surgery")
            .AddAppendectomy()
            .Build();

        // Assert
        var procedure = scenario.Procedures[0];
        var location = procedure.MutableNode["location"]?["display"]?.GetValue<string>();
        location.Should().NotBeNullOrEmpty();
        location.Should().Contain("Operating Room");
    }

    #endregion

    #region Timeline and Context Tests

    [Fact]
    public void GivenMultipleProcedures_WhenGenerated_ThenAllAddedToContext()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Surgery 1")
            .AddAppendectomy()
            .DelayWeeks(4)
            .AddEncounter("Procedure 1")
            .AddColonoscopy()
            .Build();

        // Assert
        scenario.Procedures.Should().HaveCount(2);
    }

    [Fact]
    public void GivenProcedure_WhenGenerated_ThenAppearsInTimeline()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Surgery")
            .AddAppendectomy()
            .Build();

        // Assert
        var procedureEvents = scenario.Timeline.Where(e => e.EventType == "Procedure").ToList();
        procedureEvents.Should().HaveCount(1);
    }

    [Fact]
    public void GivenProcedure_WhenGenerated_ThenAppearsInAllResources()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Surgery")
            .AddAppendectomy()
            .Build();

        // Assert
        scenario.AllResources.Should().Contain(scenario.Procedures[0]);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void GivenProcedureWithoutEncounter_WhenGenerated_ThenCreatesWithoutEncounterReference()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddProcedure(ProcedureState.Colonoscopy())
            .Build();

        // Assert
        var procedure = scenario.Procedures[0];
        var encounterRef = procedure.MutableNode["encounter"];
        encounterRef.Should().BeNull();
    }

    [Fact]
    public void GivenImagingProcedure_WhenGenerated_ThenInfersDiagnosticCategory()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Imaging")
            .AddProcedure(ProcedureState.CTScan("Chest"))
            .Build();

        // Assert
        var procedure = scenario.Procedures[0];
        var categoryCode = procedure.MutableNode["category"]?["coding"]?[0]?["code"]?.GetValue<string>();
        categoryCode.Should().Be("103693007"); // Diagnostic procedure
    }

    [Fact]
    public void GivenMRIProcedure_WhenGenerated_ThenInfersReasonableDuration()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Imaging")
            .AddProcedure(ProcedureState.MRIScan("Brain"))
            .Build();

        // Assert
        var procedure = scenario.Procedures[0];
        var start = DateTime.Parse(procedure.MutableNode["performedPeriod"]!["start"]!.GetValue<string>()!);
        var end = DateTime.Parse(procedure.MutableNode["performedPeriod"]!["end"]!.GetValue<string>()!);

        (end - start).TotalMinutes.Should().BeApproximately(45, 1);
    }

    #endregion
}
