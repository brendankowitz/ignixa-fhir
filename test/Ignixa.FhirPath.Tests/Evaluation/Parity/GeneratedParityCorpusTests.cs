using Ignixa.Abstractions;
using Ignixa.Benchmarks.Firely5;
using Ignixa.Specification.Extensions;
using System.Text.Json.Nodes;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

public class GeneratedParityCorpusTests
{
    [Fact]
    public void GivenAllSupportedVersions_WhenBuilding_ThenEveryConcreteResourceTypeIsGenerated()
    {
        // Arrange
        FhirVersion[] versions = [FhirVersion.Stu3, FhirVersion.R4, FhirVersion.R4B, FhirVersion.R5, FhirVersion.R6];

        // Act
        var corpus = GeneratedParityCorpus.Build();

        // Assert
        corpus.Select(group => group.Version).ShouldBe(versions);
        foreach (var group in corpus)
        {
            group.Resources.Select(resource => resource.ResourceType)
                .ShouldBe(group.Version.GetSchemaProvider().ResourceTypeNames.Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public void GivenAConcreteResource_WhenBuilding_ThenApplicableGenericAndConcreteExpressionsAreIncluded()
    {
        // Arrange
        var expressionCorpus = SearchParameterExpressionCorpus.Load(FhirVersion.R4);
        string genericExpression = expressionCorpus.CommonByResourceType["Resource"][0];
        string patientExpression = expressionCorpus.CommonByResourceType["Patient"][0];

        // Act
        var patient = GeneratedParityCorpus.Build()
            .Single(group => group.Version == FhirVersion.R4)
            .Resources.Single(resource => resource.ResourceType == "Patient");

        // Assert
        patient.Expressions.ShouldContain(genericExpression);
        patient.Expressions.ShouldContain(patientExpression);
    }

    [Fact]
    public void GivenFirelyFhirExtensions_WhenLoadingExpressions_ThenResolveExpressionsAreCommon()
    {
        // Arrange
        FirelyEngine.EnsureInitialized();

        // Act
        var expressionCorpus = SearchParameterExpressionCorpus.Load(FhirVersion.R4);

        // Assert
        expressionCorpus.CommonExpressions.ShouldContain(
            expression => expression.Contains("resolve()", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenASeededGeneratedResource_WhenBuildingTwice_ThenJsonIsDeterministic()
    {
        // Arrange

        // Act
        string first = GeneratedParityCorpus.BuildResource(FhirVersion.R4, "Observation").Json;
        string second = GeneratedParityCorpus.BuildResource(FhirVersion.R4, "Observation").Json;

        // Assert
        first.ShouldBe(second);
    }

    [Theory]
    [InlineData(FhirVersion.Stu3)]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenAGeneratedObservation_WhenValueBooleanIsPresent_ThenItHasABooleanShape(
        FhirVersion version)
    {
        // Arrange

        // Act
        var observation = JsonNode.Parse(GeneratedParityCorpus.BuildResource(version, "Observation").Json)!.AsObject();

        // Assert
        if (observation["valueBoolean"] is { } valueBooleanNode)
        {
            valueBooleanNode.ShouldBeAssignableTo<JsonValue>()
                .TryGetValue<bool>(out _)
                .ShouldBeTrue();
        }
    }
}
