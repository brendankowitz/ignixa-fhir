// <copyright file="JsonStructureCheckTests.cs" company="Microsoft Corporation">
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
/// Tests for JsonStructureCheck.
/// </summary>
public class JsonStructureCheckTests
{
    [Fact]
    public void GivenValidJsonObject_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new JsonStructureCheck();
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenMissingResourceType_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var json = JsonNode.Parse("{\"id\":\"123\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new JsonStructureCheck();
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "structure-1");
        Assert.Contains("resourceType", result.Issues[0].Message);
    }

    [Fact]
    public void GivenResourceTypeAsNumber_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":123}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new JsonStructureCheck();
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        // Note: When resourceType is a number, JsonNodeSourceNode can't extract it as string
        // So it's treated as missing (structure-1) rather than invalid value (structure-2)
        Assert.Contains(result.Issues, i => i.Code == "structure-1");
    }

    [Fact]
    public void GivenResourceTypeAsNull_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":null}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new JsonStructureCheck();
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "structure-1");
    }

    [Fact]
    public void GivenEmptyResourceType_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new JsonStructureCheck();
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        // Note: JsonNodeSourceNode treats resourceType as the node name, not a child property
        // So when resourceType is empty, it's equivalent to missing resourceType
        Assert.Contains(result.Issues, i => i.Code == "structure-1");
    }
}
