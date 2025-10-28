// <copyright file="TypeCheckTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Checks;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// Tests for TypeCheck.
/// </summary>
public class TypeCheckTests
{
    [Fact]
    public void GivenCorrectStringType_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"gender\":\"male\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("gender", "string");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenCorrectBooleanType_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"active\":true}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("active", "boolean");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenCorrectIntegerType_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"multipleBirthInteger\":2}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("multipleBirthInteger", "integer");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenDecimalAsInteger_WhenValidating_ThenReturnsError()
    {
        // Arrange - Note: JSON numbers are interpreted as strings by ISourceNode.Text
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"multipleBirthInteger\":2.5}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("multipleBirthInteger", "integer");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
    }

    [Fact]
    public void GivenCorrectDecimalType_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Observation\",\"value\":98.6}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("value", "decimal");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenCorrectDateFormat_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"birthDate\":\"1990-01-15\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("birthDate", "date");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenPartialDateFormat_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"birthDate\":\"1990\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("birthDate", "date");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenIncorrectDateFormat_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"birthDate\":\"not-a-date\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("birthDate", "date");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
    }

    [Fact]
    public void GivenMissingField_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"other\":\"value\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("birthDate", "date");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid); // Missing fields handled by RequiredFieldCheck
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenValidInstantWithZUtc_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"AuditEvent\",\"recorded\":\"2021-05-28T00:00:00.000Z\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("recorded", "instant");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenValidInstantWithPositiveOffset_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"AuditEvent\",\"recorded\":\"2021-05-28T13:28:17.239+02:00\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("recorded", "instant");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenValidInstantWithNegativeOffset_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"AuditEvent\",\"recorded\":\"2021-05-28T00:00:00-05:00\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("recorded", "instant");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenInstantWithoutTimezone_WhenValidating_ThenReturnsError()
    {
        // Arrange - This is the error from validate.fhir.org (line 374 of SearchDataBatch.json)
        var json = JsonNode.Parse("{\"resourceType\":\"AuditEvent\",\"recorded\":\"2021-05-28T00:00:00.000\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("recorded", "instant");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains("instant", result.Issues[0].Message);
    }

    [Fact]
    public void GivenValidAbsoluteCanonical_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"StructureDefinition\",\"url\":\"http://hl7.org/fhir/StructureDefinition/Patient|4.0.1\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("url", "canonical");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenRelativeCanonical_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"StructureDefinition\",\"url\":\"StructureDefinition/Patient\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("url", "canonical");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains("canonical", result.Issues[0].Message);
    }
}
