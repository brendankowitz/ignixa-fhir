// <copyright file="NarrativeCheckTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Tests.TestHelpers;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// Tests for NarrativeCheck.
/// </summary>
public class NarrativeCheckTests
{
    [Fact]
    public void GivenNarrativeWithStatusAndDiv_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("""
        {
          "resourceType": "Observation",
          "text": {
            "status": "generated",
            "div": "<div xmlns=\"http://www.w3.org/1999/xhtml\">Some narrative content</div>"
          },
          "status": "final",
          "code": {
            "coding": [{ "system": "http://loinc.org", "code": "29463-7" }]
          }
        }
        """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new NarrativeCheck();
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenNarrativeWithoutStatus_WhenValidatingInSpecMode_ThenReturnsError()
    {
        // Arrange
        var json = JsonNode.Parse("""
        {
          "resourceType": "Observation",
          "text": {
            "div": "<div xmlns=\"http://www.w3.org/1999/xhtml\">Some narrative content</div>"
          },
          "status": "final",
          "code": {
            "coding": [{ "system": "http://loinc.org", "code": "29463-7" }]
          }
        }
        """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new NarrativeCheck();
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "txt-1" && i.Path.Contains(".status", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenNarrativeWithoutStatus_WhenValidatingInCompatibilityMode_ThenReturnsSuccess()
    {
        // Arrange - This is the scenario from issue #219
        var json = JsonNode.Parse("""
        {
          "resourceType": "Observation",
          "text": {
            "div": "<div xmlns=\"http://www.w3.org/1999/xhtml\">Some narrative content</div>"
          },
          "status": "final",
          "code": {
            "coding": [{ "system": "http://loinc.org", "code": "29463-7" }]
          },
          "valueQuantity": { "value": 67, "unit": "kg", "system": "http://unitsofmeasure.org", "code": "kg" }
        }
        """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new NarrativeCheck();
        var settings = new ValidationSettings { Depth = ValidationDepth.Compatibility };
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenNarrativeWithInvalidStatus_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var json = JsonNode.Parse("""
        {
          "resourceType": "Observation",
          "text": {
            "status": "invalid-status",
            "div": "<div xmlns=\"http://www.w3.org/1999/xhtml\">Some narrative content</div>"
          },
          "status": "final",
          "code": {
            "coding": [{ "system": "http://loinc.org", "code": "29463-7" }]
          }
        }
        """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new NarrativeCheck();
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "txt-2");
    }

    [Fact]
    public void GivenNoNarrative_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("""
        {
          "resourceType": "Observation",
          "status": "final",
          "code": {
            "coding": [{ "system": "http://loinc.org", "code": "29463-7" }]
          }
        }
        """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new NarrativeCheck();
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenNarrativeWithEmptyStatus_WhenValidatingWithoutDiv_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("""
        {
          "resourceType": "Observation",
          "text": {
            "status": "empty"
          },
          "status": "final",
          "code": {
            "coding": [{ "system": "http://loinc.org", "code": "29463-7" }]
          }
        }
        """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new NarrativeCheck();
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenNarrativeWithGeneratedStatus_WhenValidatingWithoutDiv_ThenReturnsError()
    {
        // Arrange
        var json = JsonNode.Parse("""
        {
          "resourceType": "Observation",
          "text": {
            "status": "generated"
          },
          "status": "final",
          "code": {
            "coding": [{ "system": "http://loinc.org", "code": "29463-7" }]
          }
        }
        """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new NarrativeCheck();
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "txt-1" && i.Path.Contains(".div", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenEmptyNarrative_WhenValidatingInCompatibilityMode_ThenReturnsError()
    {
        // Arrange - Empty narrative (no status, no div) should fail even in compatibility mode
        var json = JsonNode.Parse("""
        {
          "resourceType": "Observation",
          "text": {},
          "status": "final",
          "code": {
            "coding": [{ "system": "http://loinc.org", "code": "29463-7" }]
          }
        }
        """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new NarrativeCheck();
        var settings = new ValidationSettings { Depth = ValidationDepth.Compatibility };
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "txt-1" && i.Path.Contains(".div", StringComparison.Ordinal));
    }
}
