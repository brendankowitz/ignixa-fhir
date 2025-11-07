/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for TypedElementOnSourceNode - validates choice type navigation,
 * type name normalization, and proper InstanceType resolution.
 */

using Ignixa.Serialization.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Xunit;

namespace Ignixa.Serialization.Tests.SourceNodes;

/// <summary>
/// Tests for TypedElementOnSourceNode focusing on FHIR choice type handling
/// and type name normalization per FHIRPath specification.
/// </summary>
public class TypedElementOnSourceNodeTests
{
    private readonly IStructureDefinitionSummaryProvider _r4Provider;

    public TypedElementOnSourceNodeTests()
    {
        _r4Provider = FhirSpecification.R4.GetSchemaProvider();
    }

    #region Choice Type Navigation Tests

    /// <summary>
    /// Test that navigating to 'value' (without [x] suffix) matches 'valueString' element.
    /// Per FHIR FHIRPath spec: choice elements are accessed by base name without suffix.
    /// </summary>
    [Fact]
    public void GivenObservationWithValueString_WhenNavigatingToValue_ThenReturnsValueStringElement()
    {
        // Arrange
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs1",
          "status": "final",
          "code": { "text": "test" },
          "valueString": "foo"
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act
        var valueChildren = typedElement.Children("value").ToList();

        // Assert
        Assert.Single(valueChildren);
        Assert.Equal("valueString", valueChildren[0].Name);
        Assert.Equal("string", valueChildren[0].InstanceType);
        Assert.Equal("foo", valueChildren[0].Value);
    }

    /// <summary>
    /// Test that navigating to 'value' matches 'valueInteger' element.
    /// </summary>
    [Fact]
    public void GivenObservationWithValueInteger_WhenNavigatingToValue_ThenReturnsValueIntegerElement()
    {
        // Arrange
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs2",
          "status": "final",
          "code": { "text": "test" },
          "valueInteger": 42
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act
        var valueChildren = typedElement.Children("value").ToList();

        // Assert
        Assert.Single(valueChildren);
        Assert.Equal("valueInteger", valueChildren[0].Name);
        Assert.Equal("integer", valueChildren[0].InstanceType);
        Assert.Equal(42, valueChildren[0].Value);
    }

    /// <summary>
    /// Test that navigating to 'value' matches 'valueBoolean' element.
    /// </summary>
    [Fact]
    public void GivenObservationWithValueBoolean_WhenNavigatingToValue_ThenReturnsValueBooleanElement()
    {
        // Arrange
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs3",
          "status": "final",
          "code": { "text": "test" },
          "valueBoolean": true
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act
        var valueChildren = typedElement.Children("value").ToList();

        // Assert
        Assert.Single(valueChildren);
        Assert.Equal("valueBoolean", valueChildren[0].Name);
        Assert.Equal("boolean", valueChildren[0].InstanceType);
        Assert.Equal(true, valueChildren[0].Value);
    }

    /// <summary>
    /// Test that navigating to 'value' matches 'valueQuantity' (complex type).
    /// </summary>
    [Fact]
    public void GivenObservationWithValueQuantity_WhenNavigatingToValue_ThenReturnsValueQuantityElement()
    {
        // Arrange
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs4",
          "status": "final",
          "code": { "text": "test" },
          "valueQuantity": {
            "value": 185,
            "unit": "cm",
            "system": "http://unitsofmeasure.org",
            "code": "cm"
          }
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act
        var valueChildren = typedElement.Children("value").ToList();

        // Assert
        Assert.Single(valueChildren);
        Assert.Equal("valueQuantity", valueChildren[0].Name);
        Assert.Equal("Quantity", valueChildren[0].InstanceType); // Complex types remain capitalized
    }

    /// <summary>
    /// Test that navigating to 'value' when no value[x] exists returns empty.
    /// </summary>
    [Fact]
    public void GivenObservationWithoutValue_WhenNavigatingToValue_ThenReturnsEmpty()
    {
        // Arrange
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs5",
          "status": "final",
          "code": { "text": "test" }
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act
        var valueChildren = typedElement.Children("value").ToList();

        // Assert
        Assert.Empty(valueChildren);
    }

    /// <summary>
    /// Test that direct navigation to 'valueString' also works (specific navigation).
    /// </summary>
    [Fact]
    public void GivenObservationWithValueString_WhenNavigatingToValueString_ThenReturnsElement()
    {
        // Arrange
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs1",
          "status": "final",
          "code": { "text": "test" },
          "valueString": "foo"
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act
        var valueStringChildren = typedElement.Children("valueString").ToList();

        // Assert
        Assert.Single(valueStringChildren);
        Assert.Equal("valueString", valueStringChildren[0].Name);
        Assert.Equal("string", valueStringChildren[0].InstanceType);
        Assert.Equal("foo", valueStringChildren[0].Value);
    }

    #endregion

    #region Type Normalization Tests

    /// <summary>
    /// Test that primitive type names are lowercase per FHIRPath spec.
    /// </summary>
    [Theory]
    [InlineData("valueString", "string", "test")]
    [InlineData("valueInteger", "integer", 42)]
    [InlineData("valueBoolean", "boolean", true)]
    [InlineData("valueDecimal", "decimal", "3.14")]
    [InlineData("valueUri", "uri", "http://example.com")]
    [InlineData("valueCode", "code", "final")]
    public void GivenObservationWithPrimitiveValue_WhenCheckingInstanceType_ThenReturnsLowercaseType(
        string propertyName, string expectedType, object value)
    {
        // Arrange
        // JSON serialization requires lowercase boolean values, ToLowerInvariant is intentional
#pragma warning disable CA1308 // Normalize strings to uppercase
        var observationJson = $$"""
        {
          "resourceType": "Observation",
          "id": "obs",
          "status": "final",
          "code": { "text": "test" },
          "{{propertyName}}": {{(value is string s ? $"\"{s}\"" : value.ToString()?.ToLowerInvariant())}}
        }
        """;
#pragma warning restore CA1308 // Normalize strings to uppercase

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act
        var valueChildren = typedElement.Children("value").ToList();

        // Assert
        Assert.Single(valueChildren);
        Assert.Equal(expectedType, valueChildren[0].InstanceType);
    }

    /// <summary>
    /// Test that complex type names remain capitalized.
    /// </summary>
    [Theory]
    [InlineData("valueQuantity", "Quantity")]
    [InlineData("valueCodeableConcept", "CodeableConcept")]
    [InlineData("valuePeriod", "Period")]
    [InlineData("valueRange", "Range")]
    [InlineData("valueRatio", "Ratio")]
    [InlineData("valueSampledData", "SampledData")]
    public void GivenObservationWithComplexValue_WhenCheckingInstanceType_ThenReturnsCapitalizedType(
        string propertyName, string expectedType)
    {
        // Arrange - using minimal valid complex type
        var observationJson = $$"""
        {
          "resourceType": "Observation",
          "id": "obs",
          "status": "final",
          "code": { "text": "test" },
          "{{propertyName}}": {}
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act
        var valueChildren = typedElement.Children("value").ToList();

        // Assert
        Assert.Single(valueChildren);
        Assert.Equal(expectedType, valueChildren[0].InstanceType);
    }

    #endregion

    #region Multiple Choice Elements Tests

    /// <summary>
    /// Test that multiple choice elements in a collection can be navigated correctly.
    /// Example: Observation.component can have different value[x] types.
    /// </summary>
    [Fact]
    public void GivenObservationWithMultipleComponentValues_WhenNavigatingToComponentValue_ThenReturnsAllValues()
    {
        // Arrange
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs",
          "status": "final",
          "code": { "text": "test" },
          "component": [
            {
              "code": { "text": "comp1" },
              "valueString": "text"
            },
            {
              "code": { "text": "comp2" },
              "valueInteger": 10
            },
            {
              "code": { "text": "comp3" },
              "valueQuantity": { "value": 5, "unit": "mg" }
            }
          ]
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act
        var components = typedElement.Children("component").ToList();
        var componentValues = components.SelectMany(c => c.Children("value")).ToList();

        // Assert
        Assert.Equal(3, components.Count);
        Assert.Equal(3, componentValues.Count);

        // Check types are correct
        Assert.Equal("string", componentValues[0].InstanceType);
        Assert.Equal("integer", componentValues[1].InstanceType);
        Assert.Equal("Quantity", componentValues[2].InstanceType);

        // Check values
        Assert.Equal("text", componentValues[0].Value);
        Assert.Equal(10, componentValues[1].Value);
    }

    #endregion

    #region Non-Choice Element Tests

    /// <summary>
    /// Test that non-choice elements work correctly (sanity check).
    /// </summary>
    [Fact]
    public void GivenObservation_WhenNavigatingToStatus_ThenReturnsStatusElement()
    {
        // Arrange
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs",
          "status": "final",
          "code": { "text": "test" }
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act
        var statusChildren = typedElement.Children("status").ToList();

        // Assert
        Assert.Single(statusChildren);
        Assert.Equal("status", statusChildren[0].Name);
        Assert.Equal("code", statusChildren[0].InstanceType);
        Assert.Equal("final", statusChildren[0].Value);
    }

    /// <summary>
    /// Test that navigating to complex non-choice elements works.
    /// </summary>
    [Fact]
    public void GivenObservation_WhenNavigatingToCode_ThenReturnsCodeableConceptElement()
    {
        // Arrange
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs",
          "status": "final",
          "code": {
            "coding": [{
              "system": "http://loinc.org",
              "code": "15074-8",
              "display": "Glucose"
            }]
          }
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act
        var codeChildren = typedElement.Children("code").ToList();

        // Assert
        Assert.Single(codeChildren);
        Assert.Equal("code", codeChildren[0].Name);
        Assert.Equal("CodeableConcept", codeChildren[0].InstanceType);
    }

    #endregion

    #region Edge Cases

    /// <summary>
    /// Test that navigating with null/empty name returns all children.
    /// </summary>
    [Fact]
    public void GivenObservation_WhenNavigatingWithNullName_ThenReturnsAllChildren()
    {
        // Arrange
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs",
          "status": "final",
          "code": { "text": "test" },
          "valueString": "foo"
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act
        var allChildren = typedElement.Children().ToList();

        // Assert
        Assert.True(allChildren.Count >= 4); // id, status, code, valueString at minimum
        Assert.Contains(allChildren, c => c.Name == "id");
        Assert.Contains(allChildren, c => c.Name == "status");
        Assert.Contains(allChildren, c => c.Name == "code");
        Assert.Contains(allChildren, c => c.Name == "valueString");
    }

    /// <summary>
    /// Test that navigating to non-existent element returns empty.
    /// </summary>
    [Fact]
    public void GivenObservation_WhenNavigatingToNonExistentElement_ThenReturnsEmpty()
    {
        // Arrange
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs",
          "status": "final",
          "code": { "text": "test" }
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act
        var nonExistentChildren = typedElement.Children("nonExistent").ToList();

        // Assert
        Assert.Empty(nonExistentChildren);
    }

    #endregion
}
