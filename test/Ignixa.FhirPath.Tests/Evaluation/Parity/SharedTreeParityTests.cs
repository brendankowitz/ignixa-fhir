using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

public class SharedTreeParityTests
{
    [Theory]
    [InlineData(FhirVersion.Stu3)]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenOneVersionedElementTree_WhenBothEnginesSelect_ThenPrimitiveCarrierAndValueMatch(
        FhirVersion version)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var subject = ResourceJsonNode.Parse(
            """{"resourceType":"Observation","status":"final","valueString":"1"}""").ToElement(schema);

        // Act
        var firely = FirelyEngine.Evaluate(subject, schema, "Observation.value");
        var ignixa = IgnixaEngine.Evaluate(subject, schema, "Observation.value");

        // Assert
        firely.Matches(ignixa).ShouldBeTrue(
            $"Firely returned {firely}; Ignixa returned {ignixa}.");
        firely.Results.ShouldBe(["STRING|string|1"]);
    }
}
