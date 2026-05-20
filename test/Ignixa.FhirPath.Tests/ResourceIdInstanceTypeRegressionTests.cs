using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Xunit;

namespace Ignixa.FhirPath.Tests;

public class ResourceIdInstanceTypeRegressionTests
{
    private readonly IFhirSchemaProvider _schema = FhirVersion.R5.GetSchemaProvider();

    [Fact]
    public void GivenContainedResourceId_WhenSelectingId_ThenInstanceTypeIsId()
    {
        // Arrange
        var patientJson = """
        {
          "resourceType": "Patient",
          "id": "outer",
          "contained": [
            {
              "resourceType": "Patient",
              "id": "contained1"
            }
          ]
        }
        """;
        var element = ResourceJsonNode.Parse(patientJson).ToElement(_schema);

        // Act
        var result = element.Select("Patient.contained.first().id").Single();

        // Assert
        Assert.Equal("contained1", result.Value);
        Assert.Equal("id", result.InstanceType);
    }

    [Fact]
    public void GivenResourceId_WhenSelectingId_ThenInstanceTypeIsId()
    {
        // Arrange
        var patientJson = """
        {
          "resourceType": "Patient",
          "id": "outer"
        }
        """;
        var element = ResourceJsonNode.Parse(patientJson).ToElement(_schema);

        // Act
        var result = element.Select("Patient.id").Single();

        // Assert
        Assert.Equal("outer", result.Value);
        Assert.Equal("id", result.InstanceType);
    }
}
