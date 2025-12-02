// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.FhirFakes.Lifecycle;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.Specification;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFakes.Tests;

/// <summary>
/// Tests for the PatientLifecycleGenerator class.
/// </summary>
public class PatientLifecycleGeneratorTests
{
    private readonly IFhirSchemaProvider _schemaProvider = new R4CoreSchemaProvider();

    #region Constructor and Configuration Tests

    [Fact]
    public void GivenSchemaProvider_WhenConstructing_ThenGeneratorIsCreated()
    {
        // Arrange & Act
        var generator = new PatientLifecycleGenerator(_schemaProvider);

        // Assert
        generator.Should().NotBeNull();
        generator.Events.Should().BeEmpty();
    }

    [Fact]
    public void GivenNullSchemaProvider_WhenConstructing_ThenThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => new PatientLifecycleGenerator(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("schemaProvider");
    }

    [Fact]
    public void GivenBirthYear_WhenSettingWithBirthYear_ThenBirthYearIsSet()
    {
        // Arrange
        var generator = new PatientLifecycleGenerator(_schemaProvider);

        // Act
        generator.WithBirthYear(1990);

        // Assert
        generator.BirthYear.Should().Be(1990);
    }

    [Theory]
    [InlineData(1899)]  // Below minimum
    [InlineData(2101)]  // Above maximum
    public void GivenInvalidBirthYear_WhenSettingWithBirthYear_ThenThrowsArgumentOutOfRangeException(int year)
    {
        // Arrange
        var generator = new PatientLifecycleGenerator(_schemaProvider);

        // Act
        var act = () => generator.WithBirthYear(year);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(nameof(year));
    }

    [Fact]
    public void GivenGender_WhenSettingWithGender_ThenGenderIsSet()
    {
        // Arrange
        var generator = new PatientLifecycleGenerator(_schemaProvider);

        // Act
        generator.WithGender("female");

        // Assert
        generator.Gender.Should().Be("female");
    }

    [Fact]
    public void GivenNullGender_WhenSettingWithGender_ThenThrowsArgumentNullException()
    {
        // Arrange
        var generator = new PatientLifecycleGenerator(_schemaProvider);

        // Act
        var act = () => generator.WithGender(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("gender");
    }

    #endregion

    #region Wellness Schedule Tests

    [Fact]
    public void GivenPediatricWellnessEnabled_WhenAddingWellnessSchedule_ThenPediatricScheduleIsAdded()
    {
        // Arrange
        var generator = new PatientLifecycleGenerator(_schemaProvider);

        // Act
        generator.AddWellnessSchedule(pediatric: true, adult: false);

        // Assert
        generator.Events.Should().HaveCount(1);
        generator.Events[0].Should().BeOfType<PediatricWellnessSchedule>();
    }

    [Fact]
    public void GivenAdultWellnessEnabled_WhenAddingWellnessSchedule_ThenAdultScheduleIsAdded()
    {
        // Arrange
        var generator = new PatientLifecycleGenerator(_schemaProvider);

        // Act
        generator.AddWellnessSchedule(pediatric: false, adult: true);

        // Assert
        generator.Events.Should().HaveCount(1);
        generator.Events[0].Should().BeOfType<AdultWellnessSchedule>();
    }

    [Fact]
    public void GivenBothWellnessEnabled_WhenAddingWellnessSchedule_ThenBothSchedulesAreAdded()
    {
        // Arrange
        var generator = new PatientLifecycleGenerator(_schemaProvider);

        // Act
        generator.AddWellnessSchedule(pediatric: true, adult: true);

        // Assert
        generator.Events.Should().HaveCount(2);
        generator.Events[0].Should().BeOfType<PediatricWellnessSchedule>();
        generator.Events[1].Should().BeOfType<AdultWellnessSchedule>();
    }

    #endregion

    #region Immunization Schedule Tests

    [Fact]
    public void GivenGenerator_WhenAddingImmunizationSchedule_ThenImmunizationEventIsAdded()
    {
        // Arrange
        var generator = new PatientLifecycleGenerator(_schemaProvider);

        // Act
        generator.AddImmunizationSchedule();

        // Assert
        generator.Events.Should().HaveCount(1);
        generator.Events[0].Should().BeOfType<ImmunizationScheduleEvent>();
    }

    #endregion

    #region Probabilistic Condition Tests

    [Fact]
    public void GivenConditionParameters_WhenAddingProbabilisticCondition_ThenConditionEventIsAdded()
    {
        // Arrange
        var generator = new PatientLifecycleGenerator(_schemaProvider);

        // Act
        generator.AddProbabilisticCondition(
            "Asthma",
            onsetAges: 1..17,
            probability: 0.263,
            scenarioFactory: sp => new ScenarioBuilder(sp)
                .AddConditionOnset(FhirCode.Conditions.Asthma));

        // Assert
        generator.Events.Should().HaveCount(1);
        generator.Events[0].Should().BeOfType<ProbabilisticConditionOnset>();
    }

    [Fact]
    public void GivenInvalidProbability_WhenAddingProbabilisticCondition_ThenThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var generator = new PatientLifecycleGenerator(_schemaProvider);

        // Act
        var act = () => generator.AddProbabilisticCondition(
            "Test",
            onsetAges: 1..10,
            probability: 1.5, // Invalid - greater than 1.0
            scenarioFactory: sp => new ScenarioBuilder(sp));

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("probability");
    }

    [Fact]
    public void GivenNullConditionName_WhenAddingProbabilisticCondition_ThenThrowsArgumentNullException()
    {
        // Arrange
        var generator = new PatientLifecycleGenerator(_schemaProvider);

        // Act
        var act = () => generator.AddProbabilisticCondition(
            null!,
            onsetAges: 1..10,
            probability: 0.5,
            scenarioFactory: sp => new ScenarioBuilder(sp));

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("conditionName");
    }

    #endregion

    #region Simulation Tests

    [Fact]
    public void GivenNegativeTargetAge_WhenSimulating_ThenThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var generator = new PatientLifecycleGenerator(_schemaProvider)
            .WithBirthYear(2000)
            .WithGender("male");

        // Act
        var act = () => generator.SimulateUntilAge(-1);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("targetAge");
    }

    [Fact]
    public void GivenBasicConfiguration_WhenSimulatingToAge5_ThenContextIsPopulated()
    {
        // Arrange
        var generator = new PatientLifecycleGenerator(_schemaProvider)
            .WithBirthYear(2015)
            .WithGender("male")
            .WithGivenName("John")
            .WithFamilyName("Doe");

        // Act
        var context = generator.SimulateUntilAge(5);

        // Assert
        context.Should().NotBeNull();
        context.Patient.Should().NotBeNull();
        context.BirthDate.Year.Should().Be(2015);
        context.GetAttribute<string>("gender").Should().Be("male");
        context.GetAttribute<int>("birthYear").Should().Be(2015);
    }

    [Fact]
    public void GivenPediatricWellness_WhenSimulatingToAge4_ThenWellnessVisitsAreGenerated()
    {
        // Arrange
        var generator = new PatientLifecycleGenerator(_schemaProvider)
            .WithBirthYear(2015)
            .WithGender("female")
            .AddWellnessSchedule(pediatric: true, adult: false);

        // Act
        var context = generator.SimulateUntilAge(4);

        // Assert
        context.Encounters.Should().NotBeEmpty("pediatric wellness visits at ages 1, 2, 4 should generate encounters");
    }

    [Fact]
    public void GivenImmunizationSchedule_WhenSimulatingToAge2_ThenImmunizationsAreGenerated()
    {
        // Arrange
        var generator = new PatientLifecycleGenerator(_schemaProvider)
            .WithBirthYear(2020)
            .WithGender("male")
            .AddImmunizationSchedule();

        // Act
        var context = generator.SimulateUntilAge(2);

        // Assert
        context.Immunizations.Should().NotBeEmpty("childhood immunizations should be generated for ages 0-2");
    }

    [Fact]
    public void GivenAdultWellness_WhenSimulatingToAge25_ThenAdultWellnessVisitsAreGenerated()
    {
        // Arrange
        var generator = new PatientLifecycleGenerator(_schemaProvider)
            .WithBirthYear(1995)
            .WithGender("female")
            .AddWellnessSchedule(pediatric: false, adult: true);

        // Act
        var context = generator.SimulateUntilAge(25);

        // Assert
        context.Encounters.Should().NotBeEmpty("adult wellness visits should be generated for ages 18-25");
    }

    #endregion

    #region Fluent Builder Pattern Tests

    [Fact]
    public void GivenGenerator_WhenChainingMethods_ThenReturnsSameInstance()
    {
        // Arrange
        var generator = new PatientLifecycleGenerator(_schemaProvider);

        // Act
        var result = generator
            .WithBirthYear(1990)
            .WithGender("male")
            .WithGivenName("John")
            .WithFamilyName("Doe")
            .AddWellnessSchedule(pediatric: true, adult: true)
            .AddImmunizationSchedule()
            .AddProbabilisticCondition(
                "Asthma",
                onsetAges: 1..17,
                probability: 0.263,
                scenarioFactory: sp => new ScenarioBuilder(sp).AddConditionOnset(FhirCode.Conditions.Asthma));

        // Assert
        result.Should().BeSameAs(generator);
    }

    #endregion

    #region PediatricWellnessSchedule Tests

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(4, true)]
    [InlineData(6, true)]
    [InlineData(8, true)]
    [InlineData(10, true)]
    [InlineData(12, true)]
    [InlineData(14, true)]
    [InlineData(16, true)]
    [InlineData(18, true)]
    [InlineData(0, false)]
    [InlineData(3, false)]
    [InlineData(5, false)]
    [InlineData(19, false)]
    public void GivenPediatricWellnessSchedule_WhenCheckingApplicability_ThenReturnsCorrectResult(int age, bool expected)
    {
        // Arrange
        var schedule = new PediatricWellnessSchedule();

        // Act
        var result = schedule.IsApplicable(age);

        // Assert
        result.Should().Be(expected);
    }

    #endregion

    #region AdultWellnessSchedule Tests

    [Theory]
    [InlineData(17, false)]
    [InlineData(18, true)]
    [InlineData(25, true)]
    [InlineData(50, true)]
    [InlineData(80, true)]
    public void GivenAdultWellnessSchedule_WhenCheckingApplicability_ThenReturnsCorrectResult(int age, bool expected)
    {
        // Arrange
        var schedule = new AdultWellnessSchedule();

        // Act
        var result = schedule.IsApplicable(age);

        // Assert
        result.Should().Be(expected);
    }

    #endregion

    #region ImmunizationScheduleEvent Tests

    [Theory]
    [InlineData(0, true)]   // Birth year HepB
    [InlineData(1, true)]   // Multiple childhood vaccines
    [InlineData(2, true)]   // Second doses
    [InlineData(11, true)]  // Adolescent vaccines
    [InlineData(19, true)]  // Adult flu
    [InlineData(30, true)]  // Adult flu
    public void GivenImmunizationSchedule_WhenCheckingApplicability_ThenReturnsCorrectResult(int age, bool expected)
    {
        // Arrange
        var schedule = new ImmunizationScheduleEvent();

        // Act
        var result = schedule.IsApplicable(age);

        // Assert
        result.Should().Be(expected);
    }

    #endregion

    #region ProbabilisticConditionOnset Tests

    [Theory]
    [InlineData(0, false)]  // Before onset range
    [InlineData(1, true)]   // Start of range
    [InlineData(10, true)]  // Middle of range
    [InlineData(17, true)]  // End of range
    [InlineData(18, false)] // After onset range
    public void GivenProbabilisticCondition_WhenCheckingApplicability_ThenReturnsCorrectResult(int age, bool expected)
    {
        // Arrange
        var condition = new ProbabilisticConditionOnset(
            "TestCondition",
            onsetAges: 1..17,
            probability: 0.5,
            scenarioFactory: sp => new ScenarioBuilder(sp));

        // Act
        var result = condition.IsApplicable(age);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void GivenProbabilisticConditionThatOccurred_WhenCheckingApplicability_ThenReturnsFalse()
    {
        // Arrange
        var condition = new ProbabilisticConditionOnset(
            "TestCondition",
            onsetAges: 1..17,
            probability: 1.0, // 100% chance - will always trigger
            scenarioFactory: sp => new ScenarioBuilder(sp)
                .AddConditionOnset(FhirCode.Conditions.Asthma));

        var context = new ScenarioContext
        {
            BirthDate = new DateTime(2000, 1, 1),
            CurrentTime = new DateTime(2005, 1, 1) // Age 5
        };
        context.Patient = new SchemaBasedFhirResourceFaker(_schemaProvider).Generate("Patient");

        // Act - Execute once (will trigger the condition)
        condition.Execute(context, _schemaProvider);

        // Assert - Should no longer be applicable
        condition.HasOccurred.Should().BeTrue();
        condition.IsApplicable(10).Should().BeFalse("condition already occurred");
    }

    [Fact]
    public void GivenProbabilisticCondition_WhenReset_ThenCanOccurAgain()
    {
        // Arrange
        var condition = new ProbabilisticConditionOnset(
            "TestCondition",
            onsetAges: 1..17,
            probability: 1.0,
            scenarioFactory: sp => new ScenarioBuilder(sp)
                .AddConditionOnset(FhirCode.Conditions.Asthma));

        var context = new ScenarioContext
        {
            BirthDate = new DateTime(2000, 1, 1),
            CurrentTime = new DateTime(2005, 1, 1)
        };
        context.Patient = new SchemaBasedFhirResourceFaker(_schemaProvider).Generate("Patient");

        // Trigger the condition
        condition.Execute(context, _schemaProvider);
        condition.HasOccurred.Should().BeTrue();

        // Act
        condition.Reset();

        // Assert
        condition.HasOccurred.Should().BeFalse();
        condition.IsApplicable(10).Should().BeTrue();
    }

    #endregion
}
