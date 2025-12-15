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
/// Tests for ConditionEndState functionality.
/// Uses AAA pattern and BDD naming convention.
/// </summary>
public class ConditionEndStateTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();

    #region End Condition by Attribute Tests

    [Fact]
    public void GivenConditionWithAttribute_WhenEndingByAttribute_ThenSetsResolvedStatus()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 45)
            .AddEncounter("Initial diagnosis")
            .AddConditionOnset(FhirCode.Conditions.Asthma, assignToAttribute: "asthma_condition")
            .DelayMonths(6)
            .AddEncounter("Follow-up visit")
            .EndCondition("asthma_condition")
            .Build();

        // Assert
        scenario.Conditions.Should().HaveCount(1);
        var condition = scenario.Conditions[0];

        var clinicalStatus = condition.MutableNode["clinicalStatus"]?["coding"]?[0]?["code"]?.GetValue<string>();
        clinicalStatus.Should().Be("resolved", "condition should be marked as resolved");

        var abatementDateTime = condition.MutableNode["abatementDateTime"]?.GetValue<string>();
        abatementDateTime.Should().NotBeNullOrEmpty("abatement date should be set");
    }

    [Fact]
    public void GivenConditionWithAttribute_WhenEndingWithInactiveStatus_ThenSetsInactiveStatus()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 45)
            .AddConditionOnset(FhirCode.Conditions.DiabetesType2, assignToAttribute: "diabetes_condition")
            .DelayMonths(12)
            .EndCondition("diabetes_condition", clinicalStatus: "inactive")
            .Build();

        // Assert
        var condition = scenario.Conditions[0];
        var clinicalStatus = condition.MutableNode["clinicalStatus"]?["coding"]?[0]?["code"]?.GetValue<string>();
        clinicalStatus.Should().Be("inactive", "condition should be marked as inactive");
    }

    [Fact]
    public void GivenConditionWithAttribute_WhenEndingWithRemissionStatus_ThenSetsRemissionStatus()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 45)
            .AddConditionOnset(FhirCode.Conditions.Asthma, assignToAttribute: "asthma_condition")
            .DelayMonths(6)
            .EndCondition("asthma_condition", clinicalStatus: "remission")
            .Build();

        // Assert
        var condition = scenario.Conditions[0];
        var clinicalStatus = condition.MutableNode["clinicalStatus"]?["coding"]?[0]?["code"]?.GetValue<string>();
        clinicalStatus.Should().Be("remission", "condition should be marked as in remission");
    }

    [Fact]
    public void GivenNoConditionWithAttribute_WhenEndingByAttribute_ThenThrowsException()
    {
        // Arrange & Act
        var act = () => new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .EndCondition("nonexistent_condition")
            .Build();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No condition found*");
    }

    #endregion

    #region End Condition by Code Tests

    [Fact]
    public void GivenConditionWithCode_WhenEndingByCode_ThenSetsResolvedStatus()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 45)
            .AddEncounter("Initial diagnosis")
            .AddConditionOnset(FhirCode.Conditions.Hypertension)
            .DelayMonths(12)
            .AddEncounter("Follow-up visit")
            .EndCondition(FhirCode.Conditions.Hypertension)
            .Build();

        // Assert
        scenario.Conditions.Should().HaveCount(1);
        var condition = scenario.Conditions[0];

        var clinicalStatus = condition.MutableNode["clinicalStatus"]?["coding"]?[0]?["code"]?.GetValue<string>();
        clinicalStatus.Should().Be("resolved", "condition should be marked as resolved");

        var abatementDateTime = condition.MutableNode["abatementDateTime"]?.GetValue<string>();
        abatementDateTime.Should().NotBeNullOrEmpty("abatement date should be set");
    }

    [Fact]
    public void GivenMultipleConditionsWithSameCode_WhenEndingByCode_ThenEndsMostRecent()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 45)
            .AddConditionOnset(FhirCode.Conditions.Asthma)
            .DelayWeeks(1)
            .AddConditionOnset(FhirCode.Conditions.Asthma) // Second instance
            .DelayMonths(6)
            .EndCondition(FhirCode.Conditions.Asthma) // Should end the most recent one
            .Build();

        // Assert
        scenario.Conditions.Should().HaveCount(2);

        // First condition should still be active
        var firstCondition = scenario.Conditions[0];
        var firstAbatementDateTime = firstCondition.MutableNode["abatementDateTime"]?.GetValue<string>();
        firstAbatementDateTime.Should().BeNullOrEmpty("first condition should not have abatement date");

        // Second condition should be resolved
        var secondCondition = scenario.Conditions[1];
        var secondClinicalStatus = secondCondition.MutableNode["clinicalStatus"]?["coding"]?[0]?["code"]?.GetValue<string>();
        secondClinicalStatus.Should().Be("resolved", "second (most recent) condition should be resolved");

        var secondAbatementDateTime = secondCondition.MutableNode["abatementDateTime"]?.GetValue<string>();
        secondAbatementDateTime.Should().NotBeNullOrEmpty("second condition should have abatement date");
    }

    [Fact]
    public void GivenNoConditionWithCode_WhenEndingByCode_ThenThrowsException()
    {
        // Arrange & Act
        var act = () => new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .EndCondition(FhirCode.Conditions.DiabetesType2)
            .Build();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No condition found*");
    }

    #endregion

    #region Abatement Date Tests

    [Fact]
    public void GivenCondition_WhenEnding_ThenAbatementDateMatchesCurrentTime()
    {
        // Arrange
        var startDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(startDate: startDate)
            .AddConditionOnset(FhirCode.Conditions.Asthma, assignToAttribute: "asthma_condition")
            .DelayMonths(6) // 6 months later
            .EndCondition("asthma_condition")
            .Build();

        // Assert
        var condition = scenario.Conditions[0];
        var onsetDateTime = DateTime.Parse(condition.MutableNode["onsetDateTime"]!.GetValue<string>());
        var abatementDateTime = DateTime.Parse(condition.MutableNode["abatementDateTime"]!.GetValue<string>());

        // Abatement should be approximately 6 months after onset
        (abatementDateTime - onsetDateTime).TotalDays.Should().BeApproximately(180, 5, "abatement should be ~6 months after onset");
    }

    #endregion

    #region Complex Scenario Tests

    [Fact]
    public void GivenMultipleConditions_WhenEndingSomeByAttributeAndSomeByCode_ThenCorrectConditionsAreEnded()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 50)
            .AddConditionOnset(FhirCode.Conditions.DiabetesType2, assignToAttribute: "diabetes_condition")
            .AddConditionOnset(FhirCode.Conditions.Hypertension, assignToAttribute: "hypertension_condition")
            .AddConditionOnset(FhirCode.Conditions.Asthma) // No attribute
            .DelayMonths(12)
            .EndCondition("diabetes_condition") // End by attribute
            .EndCondition(FhirCode.Conditions.Asthma) // End by code
            .Build();

        // Assert
        scenario.Conditions.Should().HaveCount(3);

        // Diabetes should be resolved
        var diabetes = scenario.Conditions[0];
        var diabetesStatus = diabetes.MutableNode["clinicalStatus"]?["coding"]?[0]?["code"]?.GetValue<string>();
        diabetesStatus.Should().Be("resolved");

        // Hypertension should still be active
        var hypertension = scenario.Conditions[1];
        var hypertensionAbatement = hypertension.MutableNode["abatementDateTime"]?.GetValue<string>();
        hypertensionAbatement.Should().BeNullOrEmpty("hypertension should still be active");

        // Asthma should be resolved
        var asthma = scenario.Conditions[2];
        var asthmaStatus = asthma.MutableNode["clinicalStatus"]?["coding"]?[0]?["code"]?.GetValue<string>();
        asthmaStatus.Should().Be("resolved");
    }

    [Fact]
    public void GivenConditionOnsetAndEnd_WhenCheckingTimeline_ThenBothEventsAreRecorded()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 45)
            .AddConditionOnset(FhirCode.Conditions.Asthma, assignToAttribute: "asthma_condition")
            .DelayMonths(6)
            .EndCondition("asthma_condition")
            .Build();

        // Assert
        scenario.Timeline.Should().HaveCountGreaterThanOrEqualTo(2, "should have onset and end events");

        var onsetEvents = scenario.Timeline.Where(e => e.EventType == "ConditionOnset").ToList();
        onsetEvents.Should().HaveCount(1, "should have one condition onset event");
    }

    #endregion

    #region Synthea-Style Scenario Tests

    [Fact]
    public void GivenAppendicitis_WhenUsingReferencedByAttribute_ThenConditionIsEnded()
    {
        // Arrange & Act - Simulates Synthea's "ConditionEnd" with "referenced_by_attribute"
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 35)
            .AddEmergencyVisit("Abdominal pain")
            .AddConditionOnset(FhirCode.Conditions.Appendicitis, assignToAttribute: "appendicitis_condition")
            .AddProcedure(ProcedureState.Appendectomy())
            .DelayDays(1) // Post-surgery
            .EndCondition("appendicitis_condition") // Condition resolved after surgery
            .Build();

        // Assert
        scenario.Conditions.Should().HaveCount(1);
        var appendicitis = scenario.Conditions[0];

        var clinicalStatus = appendicitis.MutableNode["clinicalStatus"]?["coding"]?[0]?["code"]?.GetValue<string>();
        clinicalStatus.Should().Be("resolved", "appendicitis should be resolved after surgery");

        var abatementDateTime = appendicitis.MutableNode["abatementDateTime"]?.GetValue<string>();
        abatementDateTime.Should().NotBeNullOrEmpty("abatement date should be set");
    }

    #endregion
}
