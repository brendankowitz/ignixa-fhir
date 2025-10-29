// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Xunit;

namespace Ignixa.Serialization.Tests;

/// <summary>
/// Tests for smart type resolution in JsonNodeConverter.
/// Verifies that parsing generic ResourceJsonNode returns the correct specific type
/// (e.g., ParametersJsonNode) based on the resourceType field.
/// </summary>
public class SmartResourceJsonNodeConverterTests
{
    private readonly string _parametersJson = @"{
  ""resourceType"": ""Parameters"",
  ""id"": ""example"",
  ""parameter"": [
    {
      ""name"": ""test"",
      ""valueString"": ""value""
    }
  ]
}";

    private readonly string _bundleJson = @"{
  ""resourceType"": ""Bundle"",
  ""id"": ""example"",
  ""type"": ""searchset"",
  ""total"": 1,
  ""entry"": []
}";

    private readonly string _operationOutcomeJson = @"{
  ""resourceType"": ""OperationOutcome"",
  ""id"": ""example"",
  ""issue"": [
    {
      ""severity"": ""error"",
      ""code"": ""invalid""
    }
  ]
}";

    private readonly string _searchParameterJson = @"{
  ""resourceType"": ""SearchParameter"",
  ""id"": ""example"",
  ""name"": ""test-param"",
  ""status"": ""active""
}";

    private readonly string _unknownResourceJson = @"{
  ""resourceType"": ""CustomResource"",
  ""id"": ""example"",
  ""customField"": ""value""
}";

    [Fact]
    public void GivenParametersJson_WhenParsingAsGenericResourceJsonNode_ThenReturnsParametersJsonNodeInstance()
    {
        // Act
        var resource = ResourceJsonNode.Parse(_parametersJson);

        // Assert
        Assert.NotNull(resource);
        Assert.IsType<ParametersJsonNode>(resource);
        Assert.Equal("Parameters", resource.ResourceType);
        Assert.Equal("example", resource.Id);
    }

    [Fact]
    public void GivenBundleJson_WhenParsingAsGenericResourceJsonNode_ThenReturnsBundleJsonNodeInstance()
    {
        // Act
        var resource = ResourceJsonNode.Parse(_bundleJson);

        // Assert
        Assert.NotNull(resource);
        Assert.IsType<BundleJsonNode>(resource);
        Assert.Equal("Bundle", resource.ResourceType);
        Assert.Equal("example", resource.Id);
    }

    [Fact]
    public void GivenOperationOutcomeJson_WhenParsingAsGenericResourceJsonNode_ThenReturnsOperationOutcomeJsonNodeInstance()
    {
        // Act
        var resource = ResourceJsonNode.Parse(_operationOutcomeJson);

        // Assert
        Assert.NotNull(resource);
        Assert.IsType<OperationOutcomeJsonNode>(resource);
        Assert.Equal("OperationOutcome", resource.ResourceType);
        Assert.Equal("example", resource.Id);
    }

    [Fact]
    public void GivenSearchParameterJson_WhenParsingAsGenericResourceJsonNode_ThenReturnsSearchParameterJsonNodeInstance()
    {
        // Act
        var resource = ResourceJsonNode.Parse(_searchParameterJson);

        // Assert
        Assert.NotNull(resource);
        Assert.IsType<SearchParameterJsonNode>(resource);
        Assert.Equal("SearchParameter", resource.ResourceType);
        Assert.Equal("example", resource.Id);
    }

    [Fact]
    public void GivenUnknownResourceType_WhenParsingAsGenericResourceJsonNode_ThenReturnsGenericResourceJsonNodeInstance()
    {
        // Act
        var resource = ResourceJsonNode.Parse(_unknownResourceJson);

        // Assert
        Assert.NotNull(resource);
        // For unknown types, should return generic ResourceJsonNode (not a specific subclass)
        Assert.Equal(typeof(ResourceJsonNode), resource.GetType());
        Assert.Equal("CustomResource", resource.ResourceType);
        Assert.Equal("example", resource.Id);
    }

    [Fact]
    public void GivenParametersJson_WhenParsingExplicitlyAsParametersJsonNode_ThenReturnsParametersJsonNodeInstance()
    {
        // Act - Explicit type request bypasses smart routing
        // Use the same options as ResourceJsonNode.Parse to ensure consistent behavior
        var resource = JsonSourceNodeFactory.Parse<ParametersJsonNode>(_parametersJson);

        // Assert
        Assert.NotNull(resource);
        Assert.IsType<ParametersJsonNode>(resource);
        Assert.Equal("Parameters", resource.ResourceType);
    }

    [Fact]
    public void GivenParametersJsonNode_WhenAccessingTypedProperties_ThenPropertiesAreAccessible()
    {
        // Arrange
        var resource = ResourceJsonNode.Parse(_parametersJson) as ParametersJsonNode;
        Assert.NotNull(resource);

        // Act
        var parameters = resource.Parameter;

        // Assert
        Assert.NotNull(parameters);
        Assert.Single(parameters);
        Assert.Equal("test", parameters[0].Name);
    }

    [Fact]
    public void GivenParametersResourceAsGenericType_WhenCheckingType_ThenIsCheckSucceeds()
    {
        // Arrange
        ResourceJsonNode resource = ResourceJsonNode.Parse(_parametersJson);

        // Act & Assert - Can use 'is' pattern matching
        Assert.True(resource is ParametersJsonNode);

        if (resource is ParametersJsonNode parameters)
        {
            Assert.NotNull(parameters.Parameter);
        }
        else
        {
            throw new Xunit.Sdk.XunitException("Should be ParametersJsonNode");
        }
    }

    [Fact]
    public void GivenBundleResourceAsGenericType_WhenCheckingType_ThenIsCheckSucceeds()
    {
        // Arrange
        ResourceJsonNode resource = ResourceJsonNode.Parse(_bundleJson);

        // Act & Assert - Can use 'is' pattern matching
        Assert.True(resource is BundleJsonNode);
    }

    [Fact]
    public void GivenMultipleDifferentResources_WhenParsingAsGeneric_ThenEachReturnsCorrectSpecificType()
    {
        // Act
        var parameters = ResourceJsonNode.Parse(_parametersJson);
        var bundle = ResourceJsonNode.Parse(_bundleJson);
        var operationOutcome = ResourceJsonNode.Parse(_operationOutcomeJson);

        // Assert - Each resource type returns its specific class
        Assert.IsType<ParametersJsonNode>(parameters);
        Assert.IsType<BundleJsonNode>(bundle);
        Assert.IsType<OperationOutcomeJsonNode>(operationOutcome);
    }

    [Fact]
    public void GivenResourceParsedAsGeneric_WhenAccessingMutableNode_ThenDataIsCorrect()
    {
        // Arrange & Act
        var resource = ResourceJsonNode.Parse(_parametersJson);

        // Assert
        Assert.NotNull(resource.MutableNode);
        Assert.Equal("Parameters", resource.MutableNode["resourceType"]?.GetValue<string>());
        Assert.Equal("example", resource.MutableNode["id"]?.GetValue<string>());
    }

    [Fact]
    public void GivenResourceWithoutResourceTypeField_WhenParsing_ThenCreatesFallbackInstance()
    {
        // Arrange
        var jsonWithoutType = @"{
  ""id"": ""example"",
  ""name"": ""test""
}";

        // Act
        var resource = ResourceJsonNode.Parse(jsonWithoutType);

        // Assert - Should create generic ResourceJsonNode when no resourceType is present
        Assert.NotNull(resource);
        Assert.Equal(typeof(ResourceJsonNode), resource.GetType());
        Assert.Equal("", resource.ResourceType); // No resourceType = empty string
    }

    [Fact]
    public void GivenJsonWithEmptyResourceType_WhenParsing_ThenCreatesFallbackInstance()
    {
        // Arrange
        var jsonWithEmptyType = @"{
  ""resourceType"": """",
  ""id"": ""example""
}";

        // Act
        var resource = ResourceJsonNode.Parse(jsonWithEmptyType);

        // Assert
        Assert.NotNull(resource);
        Assert.Equal(typeof(ResourceJsonNode), resource.GetType());
    }

    [Fact]
    public void GivenParametersResource_WhenSerializingAfterSmartParsing_ThenJsonRoundtripsCorrectly()
    {
        // Arrange
        var originalResource = ResourceJsonNode.Parse(_parametersJson);
        Assert.IsType<ParametersJsonNode>(originalResource);

        // Act
        var serialized = originalResource.SerializeToString();
        var reparsed = ResourceJsonNode.Parse(serialized);

        // Assert
        Assert.IsType<ParametersJsonNode>(reparsed);
        Assert.Equal("Parameters", reparsed.ResourceType);
        Assert.Equal("example", reparsed.Id);
    }

    [Fact]
    public void GivenBundleResource_WhenPolymorphicallyHandled_ThenCorrectTypeAvailableAtRuntime()
    {
        // Arrange
        ResourceJsonNode resource = ResourceJsonNode.Parse(_bundleJson);

        // Act & Assert - Polymorphic handling demonstrates the power of smart routing
        object obj = resource;

        // We can now use 'is' and pattern matching without manual casting
        if (obj is BundleJsonNode bundle)
        {
            Assert.NotNull(bundle.Type);
        }
        else
        {
            throw new Xunit.Sdk.XunitException("Should be BundleJsonNode");
        }
    }
}
