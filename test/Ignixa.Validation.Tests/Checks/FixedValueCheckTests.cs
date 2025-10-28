// <copyright file="FixedValueCheckTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

#pragma warning disable CA1861 // Avoid constant arrays as arguments

using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Checks;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// Tests for FixedValueCheck.
/// </summary>
public class FixedValueCheckTests
{
    #region Primitive Types

    [Fact]
    public void GivenFixedStringValue_WhenValueMatches_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Extension\",\"url\":\"http://example.org/ext\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FixedValueCheck("url", "\"http://example.org/ext\"");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenFixedStringValue_WhenValueDoesNotMatch_ThenReturnsError()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Extension\",\"url\":\"http://wrong.org/ext\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FixedValueCheck("url", "\"http://example.org/ext\"");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "fixed-value-mismatch");
        Assert.Contains("http://example.org/ext", result.Issues[0].Message);
    }

    [Fact]
    public void GivenFixedBooleanValue_WhenValueMatches_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"active\":true}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FixedValueCheck("active", "true");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenFixedIntegerValue_WhenValueMatches_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Observation\",\"valueInteger\":42}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FixedValueCheck("valueInteger", "42");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    #endregion

    #region Complex Types - Simple Object Fixed Values

    [Fact]
    public void GivenFixedIdentifierSystem_WhenValueMatches_ThenReturnsSuccess()
    {
        // Arrange - Fixed value on identifier.system
        var json = JsonNode.Parse(@"{
            ""resourceType"":""Patient"",
            ""identifier"":[{
                ""system"":""http://hospital.example.org"",
                ""value"":""12345""
            }]
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FixedValueCheck("identifier.system", "\"http://hospital.example.org\"");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenFixedIdentifierSystem_WhenValueDoesNotMatch_ThenReturnsError()
    {
        // Arrange - Fixed value on identifier.system but actual value differs
        var json = JsonNode.Parse(@"{
            ""resourceType"":""Patient"",
            ""identifier"":[{
                ""system"":""http://wrong.example.org"",
                ""value"":""12345""
            }]
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FixedValueCheck("identifier.system", "\"http://hospital.example.org\"");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "fixed-value-mismatch");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GivenFixedValue_WhenElementNotPresent_ThenReturnsSuccess()
    {
        // Arrange - element "url" is not present
        var json = JsonNode.Parse("{\"resourceType\":\"Extension\",\"valueString\":\"test\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FixedValueCheck("url", "\"http://example.org/ext\"");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenNullFixedValueJson_WhenConstructing_ThenThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new FixedValueCheck("url", null!));
    }

    [Fact]
    public void GivenInvalidFixedValueJson_WhenConstructing_ThenThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new FixedValueCheck("url", "{invalid json"));
    }

    [Fact]
    public void GivenNestedPath_WhenValueMatches_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""resourceType"":""Patient"",
            ""name"":[{
                ""use"":""official"",
                ""family"":""Smith""
            }]
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FixedValueCheck("name.use", "\"official\"");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    #endregion
}
