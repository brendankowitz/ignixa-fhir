// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFakes.Tests.States;

/// <summary>
/// Tests for CarePlanState. Tests care plans for care coordination scenarios.
/// Uses AAA pattern and BDD naming convention.
/// </summary>
public class CarePlanStateTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();

    #region Basic Generation Tests

    [Fact]
    public void GivenCarePlan_WhenGenerated_ThenCreatesCarePlan()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddDiabetesManagementPlan()
            .Build();

        // Assert
        scenario.CarePlans.Should().HaveCount(1);
        var carePlan = scenario.CarePlans[0];
        carePlan.ResourceType.Should().Be("CarePlan");
        carePlan.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GivenCarePlan_WhenGenerated_ThenHasActiveStatus()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddDiabetesManagementPlan()
            .Build();

        // Assert
        var carePlan = scenario.CarePlans[0];
        var status = carePlan.MutableNode["status"]?.GetValue<string>();
        status.Should().Be("active");
    }

    [Fact]
    public void GivenCarePlan_WhenGenerated_ThenHasPlanIntent()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddDiabetesManagementPlan()
            .Build();

        // Assert
        var carePlan = scenario.CarePlans[0];
        var intent = carePlan.MutableNode["intent"]?.GetValue<string>();
        intent.Should().Be("plan");
    }

    [Fact]
    public void GivenCarePlan_WhenGenerated_ThenHasTitle()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddDiabetesManagementPlan()
            .Build();

        // Assert
        var carePlan = scenario.CarePlans[0];
        var title = carePlan.MutableNode["title"]?.GetValue<string>();
        title.Should().Be("Diabetes Management Plan");
    }

    #endregion

    #region Patient Reference Tests

    [Fact]
    public void GivenCarePlan_WhenGenerated_ThenReferencesPatient()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddDiabetesManagementPlan()
            .Build();

        // Assert
        var carePlan = scenario.CarePlans[0];
        var subjectRef = carePlan.MutableNode["subject"]?["reference"]?.GetValue<string>();
        subjectRef.Should().Be($"Patient/{scenario.Patient!.Id}");
    }

    [Fact]
    public void GivenCarePlanWithoutPatient_WhenGenerated_ThenThrowsException()
    {
        // Arrange
        var faker = new SchemaBasedFhirResourceFaker(_schemaProvider);
        var context = new ScenarioContext();
        var state = CarePlanState.DiabetesManagementPlan();

        // Act & Assert
        var act = () => state.Execute(context, faker);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Patient*");
    }

    #endregion

    #region Category Tests

    [Fact]
    public void GivenCarePlan_WhenGenerated_ThenHasCategory()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddDiabetesManagementPlan()
            .Build();

        // Assert
        var carePlan = scenario.CarePlans[0];
        var categoryArray = carePlan.MutableNode["category"];
        categoryArray.Should().NotBeNull();
    }

    [Fact]
    public void GivenCarePlanWithUSCoreCategory_WhenGenerated_ThenHasAssessPlanCategory()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddDiabetesManagementPlan()
            .Build();

        // Assert
        var carePlan = scenario.CarePlans[0];
        var categoryCode = carePlan.MutableNode["category"]?[0]?["coding"]?[0]?["code"]?.GetValue<string>();
        categoryCode.Should().Be("assess-plan");
    }

    [Fact]
    public void GivenCarePlanWithMultipleCategories_WhenGenerated_ThenHasAllCategories()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddDiabetesManagementPlan()
            .Build();

        // Assert
        var carePlan = scenario.CarePlans[0];
        var categoryArray = carePlan.MutableNode["category"] as System.Text.Json.Nodes.JsonArray;
        categoryArray.Should().HaveCountGreaterOrEqualTo(1);
    }

    #endregion

    #region Period Tests

    [Fact]
    public void GivenCarePlan_WhenGenerated_ThenHasPeriodStart()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddDiabetesManagementPlan()
            .Build();

        // Assert
        var carePlan = scenario.CarePlans[0];
        var periodStart = carePlan.MutableNode["period"]?["start"]?.GetValue<string>();
        periodStart.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GivenCarePlanWithEndDate_WhenGenerated_ThenHasPeriodEnd()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddDiabetesManagementPlan()
            .Build();

        // Assert
        var carePlan = scenario.CarePlans[0];
        var periodEnd = carePlan.MutableNode["period"]?["end"]?.GetValue<string>();
        periodEnd.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Activity Tests

    [Fact]
    public void GivenCarePlanWithActivities_WhenGenerated_ThenHasActivities()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddDiabetesManagementPlan()
            .Build();

        // Assert
        var carePlan = scenario.CarePlans[0];
        var activityArray = carePlan.MutableNode["activity"] as System.Text.Json.Nodes.JsonArray;
        activityArray.Should().NotBeNull();
        activityArray!.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GivenCarePlanWithActivities_WhenGenerated_ThenActivitiesHaveStatus()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddDiabetesManagementPlan()
            .Build();

        // Assert
        var carePlan = scenario.CarePlans[0];
        var activityStatus = carePlan.MutableNode["activity"]?[0]?["detail"]?["status"]?.GetValue<string>();
        activityStatus.Should().Be("scheduled");
    }

    [Fact]
    public void GivenCarePlanWithActivities_WhenGenerated_ThenActivitiesHaveCode()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddDiabetesManagementPlan()
            .Build();

        // Assert
        var carePlan = scenario.CarePlans[0];
        var activityCode = carePlan.MutableNode["activity"]?[0]?["detail"]?["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
        activityCode.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Goal Reference Tests

    [Fact]
    public void GivenCarePlanWithGoals_WhenGenerated_ThenReferencesGoals()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddGlucoseControlGoal()
            .AddDiabetesManagementPlan()
            .Build();

        // Assert
        var carePlan = scenario.CarePlans[0];
        var goalArray = carePlan.MutableNode["goal"] as System.Text.Json.Nodes.JsonArray;
        goalArray.Should().NotBeNull();
        goalArray!.Count.Should().Be(1);

        var goalRef = goalArray[0]?["reference"]?.GetValue<string>();
        goalRef.Should().Be($"Goal/{scenario.Goals[0].Id}");
    }

    [Fact]
    public void GivenCarePlanWithMultipleGoals_WhenGenerated_ThenReferencesAllGoals()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddGlucoseControlGoal()
            .AddWeightLossGoal(10)
            .AddDiabetesManagementPlan()
            .Build();

        // Assert
        var carePlan = scenario.CarePlans[0];
        var goalArray = carePlan.MutableNode["goal"] as System.Text.Json.Nodes.JsonArray;
        goalArray.Should().NotBeNull();
        goalArray!.Count.Should().Be(2);
    }

    #endregion

    #region Factory Method Tests

    [Fact]
    public void GivenDiabetesManagementPlanFactory_WhenGenerated_ThenHasCorrectTitle()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddDiabetesManagementPlan()
            .Build();

        // Assert
        var carePlan = scenario.CarePlans[0];
        var title = carePlan.MutableNode["title"]?.GetValue<string>();
        title.Should().Be("Diabetes Management Plan");
    }

    [Fact]
    public void GivenHypertensionManagementPlanFactory_WhenGenerated_ThenHasCorrectTitle()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddHypertensionManagementPlan()
            .Build();

        // Assert
        var carePlan = scenario.CarePlans[0];
        var title = carePlan.MutableNode["title"]?.GetValue<string>();
        title.Should().Be("Hypertension Management Plan");
    }

    [Fact]
    public void GivenCardiacRehabilitationPlanFactory_WhenGenerated_ThenHasCorrectTitle()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCardiacRehabilitationPlan()
            .Build();

        // Assert
        var carePlan = scenario.CarePlans[0];
        var title = carePlan.MutableNode["title"]?.GetValue<string>();
        title.Should().Be("Cardiac Rehabilitation Plan");
    }

    [Fact]
    public void GivenWeightLossPlanFactory_WhenGenerated_ThenHasCorrectTitle()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddWeightLossPlan()
            .Build();

        // Assert
        var carePlan = scenario.CarePlans[0];
        var title = carePlan.MutableNode["title"]?.GetValue<string>();
        title.Should().Be("Weight Management Plan");
    }

    [Fact]
    public void GivenChronicPainManagementPlanFactory_WhenGenerated_ThenHasCorrectTitle()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddChronicPainManagementPlan()
            .Build();

        // Assert
        var carePlan = scenario.CarePlans[0];
        var title = carePlan.MutableNode["title"]?.GetValue<string>();
        title.Should().Be("Chronic Pain Management Plan");
    }

    [Fact]
    public void GivenPostSurgicalCarePlanFactory_WhenGenerated_ThenHasCorrectTitle()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPostSurgicalCarePlan()
            .Build();

        // Assert
        var carePlan = scenario.CarePlans[0];
        var title = carePlan.MutableNode["title"]?.GetValue<string>();
        title.Should().Be("Post-Surgical Recovery Plan");
    }

    [Fact]
    public void GivenSmokingCessationPlanFactory_WhenGenerated_ThenHasCorrectTitle()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddSmokingCessationPlan()
            .Build();

        // Assert
        var carePlan = scenario.CarePlans[0];
        var title = carePlan.MutableNode["title"]?.GetValue<string>();
        title.Should().Be("Smoking Cessation Plan");
    }

    [Fact]
    public void GivenMentalHealthCarePlanFactory_WhenGenerated_ThenHasCorrectTitle()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddMentalHealthCarePlan()
            .Build();

        // Assert
        var carePlan = scenario.CarePlans[0];
        var title = carePlan.MutableNode["title"]?.GetValue<string>();
        title.Should().Be("Mental Health Care Plan");
    }

    #endregion

    #region Description Tests

    [Fact]
    public void GivenCarePlanWithDescription_WhenGenerated_ThenHasDescription()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddDiabetesManagementPlan()
            .Build();

        // Assert
        var carePlan = scenario.CarePlans[0];
        var description = carePlan.MutableNode["description"]?.GetValue<string>();
        description.Should().NotBeNullOrEmpty();
        description.Should().Contain("Diabetes");
    }

    #endregion

    #region Timeline and Context Tests

    [Fact]
    public void GivenMultipleCarePlans_WhenGenerated_ThenAllAddedToContext()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddDiabetesManagementPlan()
            .AddHypertensionManagementPlan()
            .Build();

        // Assert
        scenario.CarePlans.Should().HaveCount(2);
    }

    [Fact]
    public void GivenCarePlan_WhenGenerated_ThenAppearsInTimeline()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddDiabetesManagementPlan()
            .Build();

        // Assert
        var carePlanEvents = scenario.Timeline.Where(e => e.EventType == "CarePlan").ToList();
        carePlanEvents.Should().HaveCount(1);
    }

    [Fact]
    public void GivenCarePlan_WhenGenerated_ThenAppearsInAllResources()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddDiabetesManagementPlan()
            .Build();

        // Assert
        scenario.AllResources.Should().Contain(scenario.CarePlans[0]);
    }

    [Fact]
    public void GivenCarePlan_WhenGenerated_ThenSetsCurrentCarePlan()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddDiabetesManagementPlan()
            .Build();

        // Assert
        scenario.CurrentCarePlan.Should().NotBeNull();
        scenario.CurrentCarePlan.Should().Be(scenario.CarePlans[0]);
    }

    [Fact]
    public void GivenMultipleCarePlans_WhenGenerated_ThenCurrentCarePlanIsLast()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddDiabetesManagementPlan()
            .AddHypertensionManagementPlan()
            .Build();

        // Assert
        scenario.CurrentCarePlan.Should().Be(scenario.CarePlans[1]);
    }

    #endregion

    #region Encounter Reference Tests

    [Fact]
    public void GivenCarePlanWithEncounter_WhenGenerated_ThenReferencesEncounter()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Care Plan Review")
            .AddDiabetesManagementPlan()
            .Build();

        // Assert
        var carePlan = scenario.CarePlans[0];
        var encounterRef = carePlan.MutableNode["encounter"]?["reference"]?.GetValue<string>();
        encounterRef.Should().Be($"Encounter/{scenario.Encounters[0].Id}");
    }

    #endregion

    #region Note Tests

    [Fact]
    public void GivenCarePlanWithNote_WhenGenerated_ThenHasNote()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddDiabetesManagementPlan()
            .Build();

        // Assert
        var carePlan = scenario.CarePlans[0];
        var noteText = carePlan.MutableNode["note"]?[0]?["text"]?.GetValue<string>();
        noteText.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Created Date Tests

    [Fact]
    public void GivenCarePlan_WhenGenerated_ThenHasCreatedDate()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddDiabetesManagementPlan()
            .Build();

        // Assert
        var carePlan = scenario.CarePlans[0];
        var created = carePlan.MutableNode["created"]?.GetValue<string>();
        created.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Author Reference Tests

    [Fact]
    public void GivenCarePlanWithPractitioner_WhenGenerated_ThenReferencesAuthor()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddFamilyPractitioner()
            .AddDiabetesManagementPlan()
            .Build();

        // Assert
        var carePlan = scenario.CarePlans[0];
        var authorRef = carePlan.MutableNode["author"]?["reference"]?.GetValue<string>();
        authorRef.Should().Be($"Practitioner/{scenario.Practitioners[0].Id}");
    }

    #endregion

    #region Attribute Assignment Tests

    [Fact]
    public void GivenCarePlanWithAttribute_WhenGenerated_ThenStoresInAttribute()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCarePlan(new CarePlanState
            {
                Title = "Test Care Plan",
                AssignToAttribute = "my_care_plan"
            })
            .Build();

        // Assert
        scenario.HasAttribute("my_care_plan").Should().BeTrue();
        var carePlanId = scenario.GetAttribute<string>("my_care_plan");
        carePlanId.Should().Be(scenario.CarePlans[0].Id);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void GivenCompleteCarePlanScenario_WhenGenerated_ThenAllResourcesAreLinked()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 55, gender: "male")
            .AddFamilyPractitioner()
            .AddConditionOnset(FhirCode.Conditions.DiabetesType2, assignToAttribute: "diabetes")
            .AddEncounter("Diabetes Management")
            .AddGlucoseControlGoal(7.0m)
            .AddWeightLossGoal(20)
            .AddDiabetesManagementPlan()
            .Build();

        // Assert
        scenario.Goals.Should().HaveCount(2);
        scenario.CarePlans.Should().HaveCount(1);

        var carePlan = scenario.CarePlans[0];

        // CarePlan references the patient
        var subjectRef = carePlan.MutableNode["subject"]?["reference"]?.GetValue<string>();
        subjectRef.Should().Be($"Patient/{scenario.Patient!.Id}");

        // CarePlan references the encounter
        var encounterRef = carePlan.MutableNode["encounter"]?["reference"]?.GetValue<string>();
        encounterRef.Should().Be($"Encounter/{scenario.Encounters[0].Id}");

        // CarePlan references the practitioner as author
        var authorRef = carePlan.MutableNode["author"]?["reference"]?.GetValue<string>();
        authorRef.Should().Be($"Practitioner/{scenario.Practitioners[0].Id}");

        // CarePlan references all goals
        var goalArray = carePlan.MutableNode["goal"] as System.Text.Json.Nodes.JsonArray;
        goalArray.Should().HaveCount(2);
    }

    #endregion
}
