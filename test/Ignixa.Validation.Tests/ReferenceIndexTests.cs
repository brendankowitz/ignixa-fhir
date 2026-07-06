// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests;

/// <summary>
/// Unit tests for <see cref="ReferenceIndex"/> contained, bundle, and miss resolution.
/// </summary>
public class ReferenceIndexTests
{
    private static IElement ToElement(string json)
    {
        var node = JsonNode.Parse(json);
        return JsonNodeSourceNode.Create(node!).ToElement(TestSchemaProvider.GetR4Schema());
    }

    [Fact]
    public void GivenContainedResource_WhenResolvingFragment_ThenReturnsContained()
    {
        // Arrange
        var element = ToElement(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""contained"": [
                { ""resourceType"": ""Practitioner"", ""id"": ""p1"" }
            ]
        }");
        var index = ReferenceIndex.Build(element);

        // Act
        var resolved = index.Resolve("#p1");

        // Assert
        resolved.ShouldNotBeNull();
        resolved!.InstanceType.ShouldBe("Practitioner");
    }

    [Fact]
    public void GivenBundle_WhenResolvingByTypeAndId_ThenReturnsEntryResource()
    {
        // Arrange
        var element = ToElement(@"{
            ""resourceType"": ""Bundle"",
            ""type"": ""collection"",
            ""entry"": [
                {
                    ""fullUrl"": ""http://example.org/fhir/Patient/1"",
                    ""resource"": { ""resourceType"": ""Patient"", ""id"": ""1"" }
                }
            ]
        }");
        var index = ReferenceIndex.Build(element);

        // Act
        var byTypeId = index.Resolve("Patient/1");
        var byFullUrl = index.Resolve("http://example.org/fhir/Patient/1");

        // Assert
        byTypeId.ShouldNotBeNull();
        byTypeId!.InstanceType.ShouldBe("Patient");
        byFullUrl.ShouldNotBeNull();
        byFullUrl!.InstanceType.ShouldBe("Patient");
    }

    [Fact]
    public void GivenBundleEntryWithVersionId_WhenResolvingVersionedReference_ThenReturnsEntryResource()
    {
        // Arrange
        var element = ToElement(@"{
            ""resourceType"": ""Bundle"",
            ""type"": ""collection"",
            ""entry"": [
                {
                    ""resource"": {
                        ""resourceType"": ""Patient"",
                        ""id"": ""1"",
                        ""meta"": { ""versionId"": ""3"" }
                    }
                }
            ]
        }");
        var index = ReferenceIndex.Build(element);

        // Act
        var resolved = index.Resolve("Patient/1/_history/3");

        // Assert
        resolved.ShouldNotBeNull();
        resolved!.InstanceType.ShouldBe("Patient");
    }

    [Fact]
    public void GivenUnknownReference_WhenResolving_ThenReturnsNull()
    {
        // Arrange
        var element = ToElement(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""contained"": [ { ""resourceType"": ""Practitioner"", ""id"": ""p1"" } ]
        }");
        var index = ReferenceIndex.Build(element);

        // Act & Assert
        index.Resolve("#missing").ShouldBeNull();
        index.Resolve("Patient/999").ShouldBeNull();
        index.Resolve(string.Empty).ShouldBeNull();
    }

    [Fact]
    public void GivenNonBundleRoot_WhenResolvingRelativeReference_ThenReturnsNull()
    {
        // Arrange
        var element = ToElement(@"{ ""resourceType"": ""Patient"", ""id"": ""1"" }");
        var index = ReferenceIndex.Build(element);

        // Act & Assert
        index.Resolve("Patient/1").ShouldBeNull();
    }
}
