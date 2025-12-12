// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Abstractions;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Specification.Generated;
using Xunit;
using Xunit.Abstractions;

namespace Ignixa.FhirFakes.Tests;

/// <summary>
/// Tests to verify that ConditionOnsetState generates resources with correct field names for STU3.
/// </summary>
public class Stu3ConditionFieldTests
{
    private readonly ITestOutputHelper _output;

    public Stu3ConditionFieldTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void GivenStu3Schema_WhenGeneratingCondition_ThenUsesContextInsteadOfEncounter()
    {
        // Arrange
        var stu3Provider = new STU3CoreSchemaProvider();
        var faker = new SchemaBasedFhirResourceFaker(stu3Provider);

        var scenario = new ScenarioBuilder(stu3Provider)
            .WithPatient(age: 35, givenName: "John", familyName: "Doe")
            .AddEncounter("Diabetes Check")
            .AddState(ConditionOnsetState.DiabetesType2())
            .Build();

        // Act
        var condition = scenario.AllResources.FirstOrDefault(r => r.ResourceType == "Condition");

        // Assert
        condition.Should().NotBeNull("Condition resource should be generated");

        var hasContext = condition!.MutableNode.TryGetPropertyValue("context", out _);
        var hasEncounter = condition.MutableNode.TryGetPropertyValue("encounter", out _);

        _output.WriteLine($"Has 'context' (STU3 field): {hasContext}");
        _output.WriteLine($"Has 'encounter' (R4+ field): {hasEncounter}");

        hasContext.Should().BeTrue("STU3 Condition should use 'context' field");
        hasEncounter.Should().BeFalse("STU3 Condition should NOT have 'encounter' field");
    }

    [Fact]
    public void GivenStu3Schema_WhenGeneratingCondition_ThenUsesAssertedDateInsteadOfRecordedDate()
    {
        // Arrange
        var stu3Provider = new STU3CoreSchemaProvider();
        var faker = new SchemaBasedFhirResourceFaker(stu3Provider);

        var scenario = new ScenarioBuilder(stu3Provider)
            .WithPatient(age: 35, givenName: "John", familyName: "Doe")
            .AddState(ConditionOnsetState.DiabetesType2())
            .Build();

        // Act
        var condition = scenario.AllResources.FirstOrDefault(r => r.ResourceType == "Condition");

        // Assert
        condition.Should().NotBeNull("Condition resource should be generated");

        var hasAssertedDate = condition!.MutableNode.TryGetPropertyValue("assertedDate", out _);
        var hasRecordedDate = condition.MutableNode.TryGetPropertyValue("recordedDate", out _);

        _output.WriteLine($"Has 'assertedDate' (STU3 field): {hasAssertedDate}");
        _output.WriteLine($"Has 'recordedDate' (R4+ field): {hasRecordedDate}");

        hasAssertedDate.Should().BeTrue("STU3 Condition should use 'assertedDate' field");
        hasRecordedDate.Should().BeFalse("STU3 Condition should NOT have 'recordedDate' field");
    }

    [Fact]
    public void GivenStu3Schema_WhenGeneratingCondition_ThenOnsetDateTimeDoesNotConflictWithOnset()
    {
        // Arrange
        var stu3Provider = new STU3CoreSchemaProvider();
        var faker = new SchemaBasedFhirResourceFaker(stu3Provider);

        var scenario = new ScenarioBuilder(stu3Provider)
            .WithPatient(age: 35, givenName: "John", familyName: "Doe")
            .AddState(ConditionOnsetState.DiabetesType2())
            .Build();

        // Act
        var condition = scenario.AllResources.FirstOrDefault(r => r.ResourceType == "Condition");

        // Assert
        condition.Should().NotBeNull("Condition resource should be generated");

        var hasOnset = condition!.MutableNode.TryGetPropertyValue("onset", out _);
        var hasOnsetDateTime = condition.MutableNode.TryGetPropertyValue("onsetDateTime", out _);

        _output.WriteLine($"Has 'onset' (base field): {hasOnset}");
        _output.WriteLine($"Has 'onsetDateTime' (choice variant): {hasOnsetDateTime}");

        hasOnset.Should().BeFalse("STU3 Condition should NOT have base 'onset' field");
        hasOnsetDateTime.Should().BeTrue("STU3 Condition should have 'onsetDateTime' choice variant");
    }

    [Fact]
    public void GivenR4Schema_WhenGeneratingCondition_ThenUsesEncounterAndRecordedDate()
    {
        // Arrange
        var r4Provider = new R4CoreSchemaProvider();
        var faker = new SchemaBasedFhirResourceFaker(r4Provider);

        var scenario = new ScenarioBuilder(r4Provider)
            .WithPatient(age: 35, givenName: "John", familyName: "Doe")
            .AddEncounter("Diabetes Check")
            .AddState(ConditionOnsetState.DiabetesType2())
            .Build();

        // Act
        var condition = scenario.AllResources.FirstOrDefault(r => r.ResourceType == "Condition");

        // Assert
        condition.Should().NotBeNull("Condition resource should be generated");

        var hasContext = condition!.MutableNode.TryGetPropertyValue("context", out _);
        var hasEncounter = condition.MutableNode.TryGetPropertyValue("encounter", out _);
        var hasAssertedDate = condition.MutableNode.TryGetPropertyValue("assertedDate", out _);
        var hasRecordedDate = condition.MutableNode.TryGetPropertyValue("recordedDate", out _);

        _output.WriteLine($"Has 'context' (STU3 field): {hasContext}");
        _output.WriteLine($"Has 'encounter' (R4+ field): {hasEncounter}");
        _output.WriteLine($"Has 'assertedDate' (STU3 field): {hasAssertedDate}");
        _output.WriteLine($"Has 'recordedDate' (R4+ field): {hasRecordedDate}");

        hasContext.Should().BeFalse("R4 Condition should NOT have 'context' field");
        hasEncounter.Should().BeTrue("R4 Condition should use 'encounter' field");
        hasAssertedDate.Should().BeFalse("R4 Condition should NOT have 'assertedDate' field");
        hasRecordedDate.Should().BeTrue("R4 Condition should use 'recordedDate' field");
    }
}
