// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Models;
using Ignixa.Serialization;
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
    public void GivenAResourceJsonNode_WhenConvertingToParameters_ThenSucceedsWithValidation()
    {
        // Arrange
        var parametersNode = ResourceJsonNode.Parse(_parametersJson);

        // Act
        var result = parametersNode.As<Parameters>();

        // Assert
        Assert.NotNull(result);
        Assert.IsType<Parameters>(result);
        Assert.Equal("Parameters", result.ResourceType);
        Assert.Equal("example", result.Id);
    }

    [Fact]
    public void GivenAResourceJsonNode_WhenConvertingToParameters_ThenSharesSameMutableNode()
    {
        // Arrange
        var parametersNode = ResourceJsonNode.Parse(_parametersJson);
        var originalMutableNode = ((IMutableJsonNode)parametersNode).MutableNode;

        // Act
        var result = parametersNode.As<Parameters>();

        // Assert - Zero-copy: both reference the same JsonObject
        Assert.Same(originalMutableNode, ((IMutableJsonNode)result).MutableNode);
    }

    [Fact]
    public void GivenAResourceJsonNode_WhenConvertingToParameters_ThenCopiesFhirVersion()
    {
        // Arrange
        var parametersNode = ResourceJsonNode.Parse(_parametersJson);
        parametersNode.FhirVersion = FhirVersion.R4;

        // Act
        var result = parametersNode.As<Parameters>();

        // Assert
        Assert.NotNull(result.FhirVersion);
        Assert.Equal(FhirVersion.R4, result.FhirVersion);
    }

    [Fact]
    public void GivenABundleResource_WhenConvertingToParameters_ThenThrowsInvalidCastException()
    {
        // Arrange
        var bundleNode = ResourceJsonNode.Parse(_parametersInvalidJson);

        // Act & Assert
        var ex = Assert.Throws<InvalidCastException>(() => bundleNode.As<Parameters>());
        Assert.Contains("Cannot convert resource of type 'Bundle'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("to Parameters", ex.Message, StringComparison.Ordinal);
        Assert.Contains("expected 'Parameters'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenAResourceWithWrongType_WhenConvertingWithoutValidation_ThenSucceeds()
    {
        // Arrange
        var bundleNode = ResourceJsonNode.Parse(_parametersInvalidJson);

        // Act
        var result = bundleNode.As<Parameters>(validate: false);

        // Assert - Conversion succeeds even though types don't match
        Assert.NotNull(result);
        Assert.IsType<Parameters>(result);
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
        var result = parametersNode.As<Parameters>();

        // Assert
        Assert.Null(result.FhirVersion);
    }

    [Fact]
    public void GivenAParametersResource_WhenAccessingParametersAfterConversion_ThenParametersAreAccessible()
    {
        // Arrange
        var parametersNode = ResourceJsonNode.Parse(_parametersJson);

        // Act
        var parametersJsonNode = parametersNode.As<Parameters>();
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
        var result = parametersNode.As<Parameters>();
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
        var result1 = parametersNode.As<Parameters>();
        var result2 = result1.As<Parameters>(); // Cast the already-converted instance

        // Assert
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.Same(result1, result2); // Same instance when already correct type (casting optimization)
        Assert.Same(((IMutableJsonNode)result1).MutableNode, ((IMutableJsonNode)result2).MutableNode); // Same underlying JsonObject
    }

    /// <summary>
    /// Test-only facade sharing "Bundle" as its post-JsonNode-suffix-strip simple name (matching
    /// ResourceTypeRegistry's "Bundle" key) but a different CLR type than the real, registered
    /// <see cref="BundleJsonNode"/> -- exercises the "registry produced a type other than T" branch of
    /// As&lt;T&gt;() (ResourceJsonNode.cs), which no other test reaches: every other As&lt;T&gt;() call in
    /// this suite either targets a registered type exactly (registry hit, type matches) or a type absent
    /// from the registry entirely (falls straight to the reflection constructor, registry never consulted).
    /// </summary>
    private sealed class BundleJsonNode : ResourceJsonNode
    {
        internal BundleJsonNode(JsonObject jsonObject)
            : base(jsonObject)
        {
        }
    }

    [Fact]
    public void GivenRegistryProducesADifferentRuntimeType_WhenConvertingViaAs_ThenFallsBackToReflectionConstructor()
    {
        // Arrange: "resourceType": "Bundle" satisfies the resource-type-name check for our LOCAL
        // BundleJsonNode, and ResourceTypeRegistry.TryCreateInstance("Bundle", ...) DOES hit -- but it
        // produces Ignixa.Serialization.BundleJsonNode (the real, hand-written one), not this local type.
        var bundleNode = ResourceJsonNode.Parse(_parametersInvalidJson);

        // Act
        var result = bundleNode.As<BundleJsonNode>();

        // Assert: the reflection-constructor fallback produced OUR type, not the registry's, and still
        // wraps the same backing node (zero-copy) despite the registry hit being discarded.
        Assert.IsType<BundleJsonNode>(result);
        Assert.Same(((IMutableJsonNode)bundleNode).MutableNode, ((IMutableJsonNode)result).MutableNode);
    }
}
