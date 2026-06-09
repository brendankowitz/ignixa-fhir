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
        var element = ResourceJsonNode.Parse(CreatePatientJson()).ToElement(_schema);

        var result = element.Select("Patient.contained.first().id").Single();

        Assert.Equal("contained1", result.Value);
        Assert.Equal("id", result.InstanceType);
    }

    [Fact]
    public void GivenResourceId_WhenSelectingId_ThenInstanceTypeIsId()
    {
        var element = ResourceJsonNode.Parse(CreatePatientJson()).ToElement(_schema);

        var result = element.Select("Patient.id").Single();

        Assert.Equal("outer", result.Value);
        Assert.Equal("id", result.InstanceType);
    }

    [Fact]
    public void GivenHumanNameElementId_WhenSelectingId_ThenInstanceTypeIsString()
    {
        var element = ResourceJsonNode.Parse(CreatePatientJson()).ToElement(_schema);

        var result = element.Select("Patient.name.id").Single();

        Assert.Equal("name1", result.Value);
        Assert.Equal("string", result.InstanceType);
    }

    [Fact]
    public void GivenIdentifierElementId_WhenSelectingId_ThenInstanceTypeIsString()
    {
        var element = ResourceJsonNode.Parse(CreatePatientJson()).ToElement(_schema);

        var result = element.Select("Patient.identifier.id").Single();

        Assert.Equal("identifier1", result.Value);
        Assert.Equal("string", result.InstanceType);
    }

    [Fact]
    public void GivenR4ResourceId_WhenSelectingId_ThenInstanceTypeIsId()
    {
        var r4Schema = FhirVersion.R4.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(CreatePatientJson()).ToElement(r4Schema);

        var result = element.Select("Patient.id").Single();

        Assert.Equal("outer", result.Value);
        Assert.Equal("id", result.InstanceType);
    }

    [Fact]
    public void GivenR4ContainedResourceId_WhenSelectingId_ThenInstanceTypeIsId()
    {
        var r4Schema = FhirVersion.R4.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(CreatePatientJson()).ToElement(r4Schema);

        var result = element.Select("Patient.contained.first().id").Single();

        Assert.Equal("contained1", result.Value);
        Assert.Equal("id", result.InstanceType);
    }

    private string CreatePatientJson()
    {
        return """
        {
          "resourceType": "Patient",
          "id": "outer",
          "contained": [
            {
              "resourceType": "Patient",
              "id": "contained1"
            }
          ],
          "name": [
            {
              "id": "name1",
              "family": "Smith"
            }
          ],
          "identifier": [
            {
              "id": "identifier1",
              "system": "http://example.org/mrn",
              "value": "12345"
            }
          ]
        }
        """;
    }
}
