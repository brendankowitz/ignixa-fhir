using System.Text.Json.Nodes;
using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

public class TargetedParityCorpusTests
{
    [Fact]
    public void GivenRequiredResourceAxes_WhenBuilding_ThenEveryFeatureIsRepresentedInEveryVersion()
    {
        // Arrange
        var requiredFeatures = Enum.GetValues<ParityResourceFeature>();
        FhirVersion[] versions = [FhirVersion.Stu3, FhirVersion.R4, FhirVersion.R4B, FhirVersion.R5, FhirVersion.R6];

        // Act
        var resources = TargetedParityCorpus.Build();

        // Assert
        foreach (var version in versions)
        {
            var represented = resources.Where(resource => resource.Version == version)
                .SelectMany(resource => resource.Features)
                .Distinct()
                .ToArray();
            represented.ShouldBe(requiredFeatures, ignoreOrder: true);
        }
    }

    [Theory]
    [InlineData(ParityResourceFeature.CardinalityZero, 0)]
    [InlineData(ParityResourceFeature.CardinalityOne, 1)]
    [InlineData(ParityResourceFeature.CardinalityMany, 3)]
    public void GivenACardinalityVariant_WhenBuilding_ThenComponentCountIsExact(
        ParityResourceFeature feature,
        int expected)
    {
        // Arrange

        // Act
        var resource = TargetedParityCorpus.Build()
            .Single(item => item.Version == FhirVersion.R4 && item.Features.Contains(feature));
        var json = JsonNode.Parse(resource.Json)!.AsObject();

        // Assert
        (json["component"]?.AsArray().Count ?? 0).ShouldBe(expected);
    }

    [Fact]
    public void GivenQuantityEquivalenceVariant_WhenBuilding_ThenSensitivityExpressionsAreIncluded()
    {
        // Arrange

        // Act
        var resource = TargetedParityCorpus.Build()
            .Single(item => item.Version == FhirVersion.R4
                            && item.Features.Contains(ParityResourceFeature.QuantityEquivalence));

        // Assert
        resource.ProbeExpressions.ShouldContain("component.value.first() ~ component.value.skip(1).first()");
        resource.ProbeExpressions.ShouldContain("component.value.skip(1).first() ~ component.value.first()");
        resource.ProbeExpressions.ShouldContain("component.value.first() ~ component.value.last()");
        resource.ProbeExpressions.ShouldContain("component.value.last() ~ component.value.first()");
    }
}
