using Shouldly;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Ignixa.Serialization.SourceNodes;
using System.Text.Json.Nodes;

namespace Ignixa.Validation.Cli.Tests;

/// <summary>
/// Basic tests for validation CLI functionality.
/// These tests verify that the validation library works correctly with different FHIR versions.
/// </summary>
public class ValidationCliTests
{
    [Fact]
    public void GivenValidPatient_WhenValidating_ThenReturnsNoErrors()
    {
        // Arrange
        var schemaProvider = new R4CoreSchemaProvider();
        var json = @"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""active"": true,
            ""name"": [{
                ""use"": ""official"",
                ""family"": ""Smith"",
                ""given"": [""John""]
            }],
            ""gender"": ""male"",
            ""birthDate"": ""1974-12-25""
        }";

        // Act
        var result = ValidateJson(schemaProvider, json);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenInvalidPatientId_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var schemaProvider = new R4CoreSchemaProvider();
        var json = @"{
            ""resourceType"": ""Patient"",
            ""id"": ""invalid id with spaces"",
            ""active"": true
        }";

        // Act
        var result = ValidateJson(schemaProvider, json);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldNotBeEmpty();
        result.Issues.ShouldContain(i => i.Path.Contains("Patient.id"));
    }

    [Fact]
    public void GivenMissingRequiredObservationFields_WhenValidating_ThenReturnsErrors()
    {
        // Arrange
        var schemaProvider = new R4CoreSchemaProvider();
        var json = @"{
            ""resourceType"": ""Observation"",
            ""id"": ""example""
        }";

        // Act
        var result = ValidateJson(schemaProvider, json);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldNotBeEmpty();
        result.HasErrors.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Patient")]
    [InlineData("Observation")]
    [InlineData("Practitioner")]
    [InlineData("Organization")]
    public void GivenDifferentResourceTypes_WhenValidatingMinimalResource_ThenValidatorWorks(string resourceType)
    {
        // Arrange
        var schemaProvider = new R4CoreSchemaProvider();
        var json = $@"{{
            ""resourceType"": ""{resourceType}"",
            ""id"": ""test""
        }}";

        // Act
        var result = ValidateJson(schemaProvider, json);

        // Assert - we just verify that validation runs without crashing
        // The result may or may not be valid depending on required fields
        result.ShouldNotBeNull();
    }

    [Fact]
    public void GivenValidationResult_WhenConvertingToOperationOutcome_ThenContainsIssues()
    {
        // Arrange
        var schemaProvider = new R4CoreSchemaProvider();
        var json = @"{
            ""resourceType"": ""Patient"",
            ""id"": ""invalid id with spaces""
        }";

        // Act
        var result = ValidateJson(schemaProvider, json);
        var operationOutcome = result.ToOperationOutcome();

        // Assert
        operationOutcome.ShouldNotBeNull();
        operationOutcome.MutableNode.ShouldNotBeNull();
        operationOutcome.MutableNode["resourceType"]?.ToString().ShouldBe("OperationOutcome");
        operationOutcome.MutableNode["issue"].ShouldNotBeNull();
    }

    private static ValidationResult ValidateJson(IFhirSchemaProvider schemaProvider, string json)
    {
        var jsonNode = JsonNode.Parse(json);
        var sourceNode = JsonNodeSourceNode.Create(jsonNode!);
        var resourceType = sourceNode.ResourceType ?? sourceNode.Name;
        var canonicalUrl = $"http://hl7.org/fhir/StructureDefinition/{resourceType}";

        var innerResolver = new StructureDefinitionSchemaResolver(schemaProvider);
        var schemaResolver = new CachedValidationSchemaResolver(innerResolver);
        var schema = schemaResolver.GetSchema(canonicalUrl);

        if (schema == null)
        {
            throw new InvalidOperationException($"Schema not found for {resourceType}");
        }

        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var state = new ValidationState();
        var element = sourceNode.ToElement(schemaProvider);

        return schema.Validate(element, settings, state);
    }
}
