// <copyright file="RequiredFieldCheckTests.cs" company="Microsoft Corporation">
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
/// Tests for RequiredFieldCheck.
/// </summary>
public class RequiredFieldCheckTests
{
    [Fact]
    public void GivenRequiredFieldPresent_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"id\":\"123\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new RequiredFieldCheck("id", isRequired: true);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenRequiredFieldMissing_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new RequiredFieldCheck("id", isRequired: true);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "required-1");
        Assert.Contains("Required field 'id' is missing", result.Issues[0].Message);
    }

    [Fact]
    public void GivenOptionalFieldMissing_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new RequiredFieldCheck("id", isRequired: false);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenFieldWithNullValue_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"id\":null}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new RequiredFieldCheck("id", isRequired: true);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        // ISourceNode filters out null values, so the field will be missing
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "required-1");
    }
}
