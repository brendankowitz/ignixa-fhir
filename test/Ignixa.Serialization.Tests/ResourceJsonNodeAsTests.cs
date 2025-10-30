// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Serialization.Tests.TestData;
using Xunit;

namespace Ignixa.Serialization.Tests;

/// <summary>
/// Tests for ResourceJsonNode.As<T>() generic conversion method.
/// Verifies zero-copy conversion, validation, and error handling.
/// </summary>
public class ResourceJsonNodeAsTests
{
    private readonly string _parametersJson = @"{
  ""resourceType"": ""Parameters"",
  ""id"": ""example"",
  ""parameter"": [
    {
      ""name"": ""resourceType"",
      ""valueString"": ""Patient""
    }
  ]
}";

    private readonly string _parametersInvalidJson = @"{
  ""resourceType"": ""Bundle"",
  ""id"": ""example"",
  ""type"": ""searchset"",
  ""total"": 1,
  ""entry"": []
}";


    [Fact]
    public void GivenAResourceJsonNode_WhenConvertingToParametersJsonNode_ThenSucceedsWithValidation()
    {
        // Arrange
        var parametersNode = ResourceJsonNode.Parse(_parametersJson);

        // Act
        var result = parametersNode.As<ParametersJsonNode>();

        // Assert
        Assert.NotNull(result);
        Assert.IsType<ParametersJsonNode>(result);
        Assert.Equal("Parameters", result.ResourceType);
        Assert.Equal("example", result.Id);
    }

    [Fact]
    public void GivenAResourceJsonNode_WhenConvertingToParametersJsonNode_ThenSharesSameMutableNode()
    {
        // Arrange
        var parametersNode = ResourceJsonNode.Parse(_parametersJson);
        var originalMutableNode = parametersNode.MutableNode;

        // Act
        var result = parametersNode.As<ParametersJsonNode>();

        // Assert - Zero-copy: both reference the same JsonObject
        Assert.Same(originalMutableNode, result.MutableNode);
    }

    [Fact]
    public void GivenAResourceJsonNode_WhenConvertingToParametersJsonNode_ThenCopiesFhirVersion()
    {
        // Arrange
        var parametersNode = ResourceJsonNode.Parse(_parametersJson);
        parametersNode.FhirVersion = FhirSpecification.R4;

        // Act
        var result = parametersNode.As<ParametersJsonNode>();

        // Assert
        Assert.NotNull(result.FhirVersion);
        Assert.Equal(FhirSpecification.R4, result.FhirVersion);
    }

    [Fact]
    public void GivenABundleResource_WhenConvertingToParametersJsonNode_ThenThrowsInvalidCastException()
    {
        // Arrange
        var bundleNode = ResourceJsonNode.Parse(_parametersInvalidJson);

        // Act & Assert
        var ex = Assert.Throws<InvalidCastException>(() => bundleNode.As<ParametersJsonNode>());
        Assert.Contains("Cannot convert resource of type 'Bundle'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ParametersJsonNode", ex.Message, StringComparison.Ordinal);
        Assert.Contains("expected 'Parameters'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenAResourceWithWrongType_WhenConvertingWithoutValidation_ThenSucceeds()
    {
        // Arrange
        var bundleNode = ResourceJsonNode.Parse(_parametersInvalidJson);

        // Act
        var result = bundleNode.As<ParametersJsonNode>(validate: false);

        // Assert - Conversion succeeds even though types don't match
        Assert.NotNull(result);
        Assert.IsType<ParametersJsonNode>(result);
        // Note: ResourceType is still "Bundle" - only the wrapper changed
        Assert.Equal("Bundle", result.ResourceType);
    }

    [Fact]
    public void GivenAResourceJsonNodeWithoutFhirVersion_WhenConverting_ThenFhirVersionIsNull()
    {
        // Arrange
        var parametersNode = ResourceJsonNode.Parse(_parametersJson);
        Assert.Null(parametersNode.FhirVersion);

        // Act
        var result = parametersNode.As<ParametersJsonNode>();

        // Assert
        Assert.Null(result.FhirVersion);
    }

    [Fact]
    public void GivenAParametersResource_WhenAccessingParametersAfterConversion_ThenParametersAreAccessible()
    {
        // Arrange
        var parametersNode = ResourceJsonNode.Parse(_parametersJson);

        // Act
        var parametersJsonNode = parametersNode.As<ParametersJsonNode>();
        var parameters = parametersJsonNode.Parameter;

        // Assert
        Assert.NotNull(parameters);
        Assert.Single(parameters);
        Assert.Equal("resourceType", parameters[0].Name);
    }

    [Fact]
    public void GivenAConvertedResource_WhenModifyingMutableNode_ThenChangesAreReflectedInBoth()
    {
        // Arrange
        var parametersNode = ResourceJsonNode.Parse(_parametersJson);
        var originalId = parametersNode.Id;

        // Act
        var result = parametersNode.As<ParametersJsonNode>();
        result.Id = "modified";

        // Assert - Both reference the same underlying JsonObject
        Assert.Equal("modified", parametersNode.Id);
        Assert.Equal("modified", result.Id);
    }

    [Fact]
    public void GivenAGenericResourceJsonNode_WhenConvertingMultipleTimes_ThenEachConversionSucceeds()
    {
        // Arrange
        var parametersNode = ResourceJsonNode.Parse(_parametersJson);

        // Act
        var result1 = parametersNode.As<ParametersJsonNode>();
        var result2 = result1.As<ParametersJsonNode>(); // Cast the already-converted instance

        // Assert
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.Same(result1, result2); // Same instance when already correct type (casting optimization)
        Assert.Same(result1.MutableNode, result2.MutableNode); // Same underlying JsonObject
    }
}
