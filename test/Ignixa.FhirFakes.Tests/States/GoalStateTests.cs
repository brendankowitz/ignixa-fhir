// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFakes.Tests.States;

/// <summary>
/// Tests for GoalState. Tests health outcome goals for care coordination.
/// Uses AAA pattern and BDD naming convention.
/// </summary>
public class GoalStateTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();

    #region Basic Generation Tests

    [Fact]
    public void GivenGoal_WhenGenerated_ThenCreatesGoal()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddGlucoseControlGoal()
            .Build();

        // Assert
        scenario.Goals.Should().HaveCount(1);
        var goal = scenario.Goals[0];
        goal.ResourceType.Should().Be("Goal");
        goal.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GivenGoal_WhenGenerated_ThenHasActiveStatus()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddGlucoseControlGoal()
            .Build();

        // Assert
        var goal = scenario.Goals[0];
        var status = goal.MutableNode["lifecycleStatus"]?.GetValue<string>();
        status.Should().Be("active");
    }

    [Fact]
    public void GivenGoal_WhenGenerated_ThenHasDescription()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddGlucoseControlGoal()
            .Build();

        // Assert
        var goal = scenario.Goals[0];
        var descriptionCode = goal.MutableNode["description"]?["coding"]?[0]?["code"]?.GetValue<string>();
        descriptionCode.Should().Be("698360004"); // Glucose level control
    }

    #endregion

    #region Patient Reference Tests

    [Fact]
    public void GivenGoal_WhenGenerated_ThenReferencesPatient()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddGlucoseControlGoal()
            .Build();

        // Assert
        var goal = scenario.Goals[0];
        var subjectRef = goal.MutableNode["subject"]?["reference"]?.GetValue<string>();
        subjectRef.Should().Be($"Patient/{scenario.Patient!.Id}");
    }

    [Fact]
    public void GivenGoalWithoutPatient_WhenGenerated_ThenThrowsException()
    {
        // Arrange
        var faker = new SchemaBasedFhirResourceFaker(_schemaProvider);
        var context = new ScenarioContext();
        var state = GoalState.GlucoseControlGoal();

        // Act & Assert
        var act = () => state.Execute(context, faker);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Patient*");
    }

    #endregion

    #region Priority Tests

    [Fact]
    public void GivenHighPriorityGoal_WhenGenerated_ThenHasHighPriority()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddBloodPressureControlGoal()
            .Build();

        // Assert
        var goal = scenario.Goals[0];
        var priorityCode = goal.MutableNode["priority"]?["coding"]?[0]?["code"]?.GetValue<string>();
        priorityCode.Should().Be("high-priority");
    }

    [Fact]
    public void GivenMediumPriorityGoal_WhenGenerated_ThenHasMediumPriority()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddExerciseGoal()
            .Build();

        // Assert
        var goal = scenario.Goals[0];
        var priorityCode = goal.MutableNode["priority"]?["coding"]?[0]?["code"]?.GetValue<string>();
        priorityCode.Should().Be("medium-priority");
    }

    #endregion

    #region Achievement Status Tests

    [Fact]
    public void GivenGoalWithAchievementStatus_WhenGenerated_ThenHasAchievementStatus()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddGlucoseControlGoal()
            .Build();

        // Assert
        var goal = scenario.Goals[0];
        var achievementCode = goal.MutableNode["achievementStatus"]?["coding"]?[0]?["code"]?.GetValue<string>();
        achievementCode.Should().Be("in-progress");
    }

    [Fact]
    public void GivenAchievedGoal_WhenGenerated_ThenHasAchievedStatus()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddGoal(GoalState.AchievedGoal(GoalState.GoalCodes.WeightLoss))
            .Build();

        // Assert
        var goal = scenario.Goals[0];
        var lifecycleStatus = goal.MutableNode["lifecycleStatus"]?.GetValue<string>();
        lifecycleStatus.Should().Be("completed");

        var achievementCode = goal.MutableNode["achievementStatus"]?["coding"]?[0]?["code"]?.GetValue<string>();
        achievementCode.Should().Be("achieved");
    }

    #endregion

    #region Target Tests

    [Fact]
    public void GivenGoalWithTarget_WhenGenerated_ThenHasTarget()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddGlucoseControlGoal(a1c: 7.0m)
            .Build();

        // Assert
        var goal = scenario.Goals[0];
        var targetArray = goal.MutableNode["target"];
        targetArray.Should().NotBeNull();

        var measureCode = targetArray?[0]?["measure"]?["coding"]?[0]?["code"]?.GetValue<string>();
        measureCode.Should().Be("4548-4"); // HbA1c LOINC code
    }

    [Fact]
    public void GivenGoalWithTargetValue_WhenGenerated_ThenHasTargetQuantity()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddGlucoseControlGoal(a1c: 6.5m)
            .Build();

        // Assert
        var goal = scenario.Goals[0];
        var targetValue = goal.MutableNode["target"]?[0]?["detailQuantity"]?["value"]?.GetValue<decimal>();
        targetValue.Should().Be(6.5m);

        var unit = goal.MutableNode["target"]?[0]?["detailQuantity"]?["unit"]?.GetValue<string>();
        unit.Should().Be("%");
    }

    [Fact]
    public void GivenGoalWithTargetDate_WhenGenerated_ThenHasDueDate()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddGlucoseControlGoal()
            .Build();

        // Assert
        var goal = scenario.Goals[0];
        var dueDate = goal.MutableNode["target"]?[0]?["dueDate"]?.GetValue<string>();
        dueDate.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Start Date Tests

    [Fact]
    public void GivenGoal_WhenGenerated_ThenHasStartDate()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddGlucoseControlGoal()
            .Build();

        // Assert
        var goal = scenario.Goals[0];
        var startDate = goal.MutableNode["startDate"]?.GetValue<string>();
        startDate.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Factory Method Tests

    [Fact]
    public void GivenWeightLossGoalFactory_WhenGenerated_ThenHasCorrectTarget()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddWeightLossGoal(20)
            .Build();

        // Assert
        var goal = scenario.Goals[0];
        var targetValue = goal.MutableNode["target"]?[0]?["detailQuantity"]?["value"]?.GetValue<decimal>();
        targetValue.Should().Be(20m);

        var descriptionCode = goal.MutableNode["description"]?["coding"]?[0]?["code"]?.GetValue<string>();
        descriptionCode.Should().Be("289169006"); // Weight loss SNOMED code
    }

    [Fact]
    public void GivenBloodPressureControlGoalFactory_WhenGenerated_ThenHasCorrectTarget()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddBloodPressureControlGoal(120)
            .Build();

        // Assert
        var goal = scenario.Goals[0];
        var targetValue = goal.MutableNode["target"]?[0]?["detailQuantity"]?["value"]?.GetValue<decimal>();
        targetValue.Should().Be(120m);

        var unit = goal.MutableNode["target"]?[0]?["detailQuantity"]?["unit"]?.GetValue<string>();
        unit.Should().Be("mm[Hg]");
    }

    [Fact]
    public void GivenSmokingCessationGoalFactory_WhenGenerated_ThenHasCorrectDescription()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddSmokingCessationGoal()
            .Build();

        // Assert
        var goal = scenario.Goals[0];
        var descriptionCode = goal.MutableNode["description"]?["coding"]?[0]?["code"]?.GetValue<string>();
        descriptionCode.Should().Be("160617001"); // Stopped smoking SNOMED code
    }

    [Fact]
    public void GivenExerciseGoalFactory_WhenGenerated_ThenHasCorrectDescription()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddExerciseGoal(200)
            .Build();

        // Assert
        var goal = scenario.Goals[0];
        var descriptionCode = goal.MutableNode["description"]?["coding"]?[0]?["code"]?.GetValue<string>();
        descriptionCode.Should().Be("226029004"); // Physical activity SNOMED code

        var note = goal.MutableNode["note"]?[0]?["text"]?.GetValue<string>();
        note.Should().Contain("200 minutes");
    }

    [Fact]
    public void GivenPainReductionGoalFactory_WhenGenerated_ThenHasCorrectTarget()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPainReductionGoal(2)
            .Build();

        // Assert
        var goal = scenario.Goals[0];
        var targetValue = goal.MutableNode["target"]?[0]?["detailQuantity"]?["value"]?.GetValue<decimal>();
        targetValue.Should().Be(2m);

        var comparator = goal.MutableNode["target"]?[0]?["detailQuantity"]?["comparator"]?.GetValue<string>();
        comparator.Should().Be("<=");
    }

    [Fact]
    public void GivenMobilityImprovementGoalFactory_WhenGenerated_ThenHasCorrectDescription()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddMobilityImprovementGoal()
            .Build();

        // Assert
        var goal = scenario.Goals[0];
        var descriptionCode = goal.MutableNode["description"]?["coding"]?[0]?["code"]?.GetValue<string>();
        descriptionCode.Should().Be("249868004"); // Mobility SNOMED code
    }

    [Fact]
    public void GivenMedicationAdherenceGoalFactory_WhenGenerated_ThenHasCorrectDescription()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddMedicationAdherenceGoal()
            .Build();

        // Assert
        var goal = scenario.Goals[0];
        var descriptionCode = goal.MutableNode["description"]?["coding"]?[0]?["code"]?.GetValue<string>();
        descriptionCode.Should().Be("418284009"); // Medication compliance SNOMED code
    }

    #endregion

    #region Timeline and Context Tests

    [Fact]
    public void GivenMultipleGoals_WhenGenerated_ThenAllAddedToContext()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddGlucoseControlGoal()
            .AddBloodPressureControlGoal()
            .AddWeightLossGoal(10)
            .Build();

        // Assert
        scenario.Goals.Should().HaveCount(3);
    }

    [Fact]
    public void GivenGoal_WhenGenerated_ThenAppearsInTimeline()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddGlucoseControlGoal()
            .Build();

        // Assert
        var goalEvents = scenario.Timeline.Where(e => e.EventType == "Goal").ToList();
        goalEvents.Should().HaveCount(1);
    }

    [Fact]
    public void GivenGoal_WhenGenerated_ThenAppearsInAllResources()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddGlucoseControlGoal()
            .Build();

        // Assert
        scenario.AllResources.Should().Contain(scenario.Goals[0]);
    }

    [Fact]
    public void GivenGoal_WhenGenerated_ThenSetsCurrentGoal()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddGlucoseControlGoal()
            .Build();

        // Assert
        scenario.CurrentGoal.Should().NotBeNull();
        scenario.CurrentGoal.Should().Be(scenario.Goals[0]);
    }

    [Fact]
    public void GivenMultipleGoals_WhenGenerated_ThenCurrentGoalIsLast()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddGlucoseControlGoal()
            .AddBloodPressureControlGoal()
            .Build();

        // Assert
        scenario.CurrentGoal.Should().Be(scenario.Goals[1]);
    }

    #endregion

    #region Category Tests

    [Fact]
    public void GivenGoal_WhenGenerated_ThenHasCategory()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddGlucoseControlGoal()
            .Build();

        // Assert
        var goal = scenario.Goals[0];
        var categoryArray = goal.MutableNode["category"];
        categoryArray.Should().NotBeNull();
    }

    #endregion

    #region Note Tests

    [Fact]
    public void GivenGoalWithNote_WhenGenerated_ThenHasNote()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddGlucoseControlGoal()
            .Build();

        // Assert
        var goal = scenario.Goals[0];
        var noteText = goal.MutableNode["note"]?[0]?["text"]?.GetValue<string>();
        noteText.Should().NotBeNullOrEmpty();
        noteText.Should().Contain("HbA1c");
    }

    #endregion

    #region Attribute Assignment Tests

    [Fact]
    public void GivenGoalWithAttribute_WhenGenerated_ThenStoresInAttribute()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddGoal(new GoalState
            {
                Description = GoalState.GoalCodes.GlucoseControl,
                AssignToAttribute = "diabetes_goal"
            })
            .Build();

        // Assert
        scenario.HasAttribute("diabetes_goal").Should().BeTrue();
        var goalId = scenario.GetAttribute<string>("diabetes_goal");
        goalId.Should().Be(scenario.Goals[0].Id);
    }

    #endregion
}
