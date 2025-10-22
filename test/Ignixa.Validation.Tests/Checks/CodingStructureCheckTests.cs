// <copyright file="CodingStructureCheckTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json.Nodes;
using Ignixa.SourceNodeSerialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Checks;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// Tests for CodingStructureCheck.
/// </summary>
public class CodingStructureCheckTests
{
    [Fact]
    public void GivenCodingWithAbsoluteSystem_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse(
            "{\"resourceType\":\"Patient\",\"maritalStatus\":{\"coding\":[{\"system\":\"http://terminology.hl7.org/CodeSystem/v3-MaritalStatus\",\"code\":\"M\"}]}}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new CodingStructureCheck("maritalStatus");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenCodingWithUrnSystem_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse(
            "{\"resourceType\":\"Patient\",\"maritalStatus\":{\"coding\":[{\"system\":\"urn:iso:std:iec:61883-6:amdl\",\"code\":\"M\"}]}}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new CodingStructureCheck("maritalStatus");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenCodingWithRelativeSystem_WhenValidating_ThenReturnsError()
    {
        // Arrange - This is the error from validate.fhir.org (line 152 of SearchDataBatch.json)
        var json = JsonNode.Parse(
            "{\"resourceType\":\"Encounter\",\"class\":{\"coding\":[{\"system\":\"system\",\"code\":\"code\"}]}}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new CodingStructureCheck("class");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Equal("coding-system-absolute", result.Issues[0].Code);
        Assert.Contains("must be an absolute reference", result.Issues[0].Message);
    }

    [Fact]
    public void GivenCodingWithoutSystemOrCode_WhenValidating_ThenReturnsWarning()
    {
        // Arrange - Coding with neither system nor code (only display)
        var json = JsonNode.Parse(
            "{\"resourceType\":\"Patient\",\"maritalStatus\":{\"coding\":[{\"display\":\"Married\"}]}}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new CodingStructureCheck("maritalStatus");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        // CodingStructureCheck returns failure if any issues exist (including warnings)
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Equal("coding-1", result.Issues[0].Code);
        Assert.Equal(IssueSeverity.Warning, result.Issues[0].Severity);
    }

    [Fact]
    public void GivenCodeableConceptWithMultipleCodings_WhenValidating_ThenValidatesAllCodings()
    {
        // Arrange - Mix of valid and invalid codings
        var json = JsonNode.Parse(
            "{\"resourceType\":\"Patient\",\"maritalStatus\":{\"coding\":[{\"system\":\"http://terminology.hl7.org/CodeSystem/v3-MaritalStatus\",\"code\":\"M\"},{\"system\":\"localSystem\",\"code\":\"M\"}]}}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new CodingStructureCheck("maritalStatus");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues); // One error for the local system
        Assert.Equal("coding-system-absolute", result.Issues[0].Code);
    }

    [Fact]
    public void GivenCodeableConceptWithoutCoding_WhenValidating_ThenReturnsWarning()
    {
        // Arrange - CodeableConcept with only text (no coding array, no system or code)
        // This is treated as a bare Coding without system or code, which produces a warning
        var json = JsonNode.Parse(
            "{\"resourceType\":\"Patient\",\"maritalStatus\":{\"text\":\"Married\"}}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new CodingStructureCheck("maritalStatus");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        // CodingStructureCheck treats missing coding array as a direct Coding
        // and warns if no system and code are present
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Equal("coding-1", result.Issues[0].Code);
        Assert.Equal(IssueSeverity.Warning, result.Issues[0].Severity);
    }
}
