// <copyright file="CardinalityCheckTests.cs" company="Microsoft Corporation">
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
/// Tests for CardinalityCheck.
/// </summary>
public class CardinalityCheckTests
{
    [Fact]
    public void GivenCardinalityWithinRange_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"name\":[{\"family\":\"Doe\"}]}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new CardinalityCheck("name", min: 0, max: 5);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenCardinalityBelowMinimum_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"other\":\"value\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new CardinalityCheck("name", min: 1, max: 5);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "cardinality-violation");
    }

    [Fact]
    public void GivenCardinalityAboveMaximum_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"name\":[{\"family\":\"Doe\"},{\"family\":\"Smith\"},{\"family\":\"Jones\"}]}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new CardinalityCheck("name", min: 0, max: 2);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "cardinality-violation");
    }

    [Fact]
    public void GivenUnboundedCardinality_WhenValidating_ThenAcceptsAnyCount()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"name\":[{},{},{},{},{}]}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new CardinalityCheck("name", min: 0, max: null);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenSingleValueForArrayElement_WhenValidating_ThenCountsAsOne()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"gender\":\"male\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new CardinalityCheck("gender", min: 1, max: 1);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenEmptyArray_WhenValidating_ThenCountsAsZero()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"name\":[]}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new CardinalityCheck("name", min: 1, max: 5);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
    }
}
