// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFakes.Tests;

/// <summary>
/// Tests for TerminalState. Tests scenario termination with various reasons.
/// Uses AAA pattern and BDD naming convention.
/// </summary>
public class TerminalStateTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();

    #region Basic Terminal State Tests

    [Fact]
    public void GivenTerminalStateCompleted_WhenExecuted_ThenSetsCompletedFlag()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .Complete()
            .Build();

        // Assert
        scenario.Attributes.Should().ContainKey("scenario_completed");
        scenario.Attributes["scenario_completed"].Should().Be(true);
    }

    [Fact]
    public void GivenTerminalStateCompleted_WhenExecuted_ThenSetsReasonToCompleted()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .Complete()
            .Build();

        // Assert
        scenario.Attributes.Should().ContainKey("terminal_reason");
        scenario.Attributes["terminal_reason"].Should().Be("Completed");
    }

    [Fact]
    public void GivenTerminalStateDeath_WhenExecuted_ThenSetsCompletedFlag()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .Death()
            .Build();

        // Assert
        scenario.Attributes.Should().ContainKey("scenario_completed");
        scenario.Attributes["scenario_completed"].Should().Be(true);
    }

    [Fact]
    public void GivenTerminalStateDeath_WhenExecuted_ThenSetsReasonToDeath()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .Death()
            .Build();

        // Assert
        scenario.Attributes.Should().ContainKey("terminal_reason");
        scenario.Attributes["terminal_reason"].Should().Be("Death");
    }

    [Fact]
    public void GivenTerminalStateCustom_WhenExecuted_ThenSetsCustomReason()
    {
        // Arrange & Act
        var customReason = "Patient moved to another facility";
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .Terminal(customReason)
            .Build();

        // Assert
        scenario.Attributes.Should().ContainKey("terminal_reason");
        scenario.Attributes["terminal_reason"].Should().Be(customReason);
    }

    #endregion

    #region Factory Method Tests

    [Fact]
    public void GivenTerminalStateCompletedFactory_WhenCreated_ThenHasCompletedReason()
    {
        // Arrange & Act
        var state = TerminalState.Completed();

        // Assert
        state.Reason.Should().Be("Completed");
    }

    [Fact]
    public void GivenTerminalStateDeathFactory_WhenCreated_ThenHasDeathReason()
    {
        // Arrange & Act
        var state = TerminalState.Death();

        // Assert
        state.Reason.Should().Be("Death");
    }

    [Fact]
    public void GivenTerminalStateCustomFactory_WhenCreated_ThenHasCustomReason()
    {
        // Arrange & Act
        var customReason = "Lost to follow-up";
        var state = TerminalState.Custom(customReason);

        // Assert
        state.Reason.Should().Be(customReason);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void GivenScenarioWithTerminalState_WhenBuilt_ThenResourcesGeneratedBeforeTerminal()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Initial visit")
            .AddObservation(ObservationState.BloodPressure(120, 80))
            .Complete()
            .Build();

        // Assert
        scenario.Encounters.Should().HaveCount(1);
        scenario.Observations.Should().HaveCount(1);
        scenario.Attributes["scenario_completed"].Should().Be(true);
    }

    [Fact]
    public void GivenScenarioWithDeathTerminal_WhenBuilt_ThenResourcesGeneratedBeforeDeath()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Emergency visit")
            .Death()
            .Build();

        // Assert
        scenario.Encounters.Should().HaveCount(1);
        scenario.Attributes["terminal_reason"].Should().Be("Death");
    }

    [Fact]
    public void GivenScenarioWithMultipleStatesBeforeTerminal_WhenBuilt_ThenAllStatesExecuted()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 65)
            .AddWellnessVisit("Annual checkup")
            .AddObservation(ObservationState.BloodPressure(130, 85))
            .AddObservation(ObservationState.BodyWeight(75))
            .DelayDays(7)
            .AddEncounter("Follow-up visit")
            .Complete()
            .Build();

        // Assert
        scenario.Encounters.Should().HaveCount(2);
        scenario.Observations.Should().HaveCount(2);
        scenario.Attributes["scenario_completed"].Should().Be(true);
        scenario.Attributes["terminal_reason"].Should().Be("Completed");
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void GivenTerminalStateOnly_WhenBuilt_ThenSetsAttributesWithoutResources()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .Complete()
            .Build();

        // Assert
        scenario.AllResources.Should().HaveCount(1); // Only patient exists in AllResources
        scenario.Attributes["scenario_completed"].Should().Be(true);
    }

    [Fact]
    public void GivenMultipleTerminalStates_WhenBuilt_ThenLastOneWins()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .Complete()
            .Death() // This should overwrite "Completed"
            .Build();

        // Assert
        scenario.Attributes["terminal_reason"].Should().Be("Death");
    }

    [Fact]
    public void GivenTerminalStateWithEmptyReason_WhenBuilt_ThenUsesEmptyString()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .Terminal(string.Empty)
            .Build();

        // Assert
        scenario.Attributes["terminal_reason"].Should().Be(string.Empty);
    }

    #endregion

    #region Scenario Context Attribute Tests

    [Fact]
    public void GivenTerminalState_WhenExecuted_ThenCanQueryCompletionStatus()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .Complete()
            .Build();

        // Assert
        var isCompleted = scenario.GetAttribute<bool>("scenario_completed");
        isCompleted.Should().BeTrue();
    }

    [Fact]
    public void GivenTerminalState_WhenExecuted_ThenCanQueryTerminalReason()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .Terminal("Transferred to specialist")
            .Build();

        // Assert
        var reason = scenario.GetAttribute<string>("terminal_reason");
        reason.Should().Be("Transferred to specialist");
    }

    [Fact]
    public void GivenNoTerminalState_WhenBuilt_ThenCompletionFlagNotSet()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Visit")
            .Build();

        // Assert
        scenario.Attributes.Should().NotContainKey("scenario_completed");
        scenario.Attributes.Should().NotContainKey("terminal_reason");
    }

    #endregion

    #region Real-World Scenario Tests

    [Fact]
    public void GivenChronicDiseaseScenario_WhenCompleted_ThenMarkedAsComplete()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithName("Type 2 Diabetes Management")
            .WithPatient(age: 55, gender: "male")
            .AddWellnessVisit("Initial diagnosis")
            .AddObservation(ObservationState.HemoglobinA1c())
            .DelayMonths(3)
            .AddWellnessVisit("Follow-up")
            .AddObservation(ObservationState.HemoglobinA1c())
            .DelayMonths(3)
            .AddWellnessVisit("Follow-up")
            .AddObservation(ObservationState.HemoglobinA1c())
            .Complete()
            .Build();

        // Assert
        scenario.Observations.Should().HaveCount(3);
        scenario.Encounters.Should().HaveCount(3);
        scenario.Attributes["scenario_completed"].Should().Be(true);
        scenario.Attributes["terminal_reason"].Should().Be("Completed");
    }

    [Fact]
    public void GivenAcuteConditionWithDeath_WhenTerminated_ThenMarkedAsDeath()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithName("Acute Emergency Visit")
            .WithPatient(age: 72, gender: "male")
            .AddEmergencyVisit("Chest pain")
            .AddObservation(ObservationState.HeartRate(140))
            .DelayDays(1)
            .Death()
            .Build();

        // Assert
        scenario.Encounters.Should().HaveCount(1);
        scenario.Observations.Should().HaveCount(1);
        scenario.Attributes["scenario_completed"].Should().Be(true);
        scenario.Attributes["terminal_reason"].Should().Be("Death");
    }

    #endregion
}
