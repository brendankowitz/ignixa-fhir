// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Shouldly;
using Xunit;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Unit tests for <see cref="ReferenceIndex"/> contained, bundle, and miss resolution.
/// </summary>
public class ReferenceIndexTests
{
    private readonly IFhirSchemaProvider _r4Provider = FhirVersion.R4.GetSchemaProvider();

    private IElement ToElement(string json) =>
        ResourceJsonNode.Parse(json).ToElement(_r4Provider);

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

    [Fact]
    public void GivenResourceRoot_WhenResolvingBareHash_ThenReturnsRootElement()
    {
        // Arrange
        var element = ToElement(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""contained"": [ { ""resourceType"": ""Practitioner"", ""id"": ""p1"" } ]
        }");
        var index = ReferenceIndex.Build(element);

        // Act
        var resolved = index.Resolve("#");

        // Assert
        resolved.ShouldBeSameAs(element);
    }

    [Fact]
    public void GivenBundleRoot_WhenResolvingBareHash_ThenReturnsBundleItself()
    {
        // Arrange
        var element = ToElement(@"{
            ""resourceType"": ""Bundle"",
            ""type"": ""collection"",
            ""entry"": [
                {
                    ""resource"": { ""resourceType"": ""Patient"", ""id"": ""1"" }
                }
            ]
        }");
        var index = ReferenceIndex.Build(element);

        // Act
        var resolved = index.Resolve("#");

        // Assert
        resolved.ShouldBeSameAs(element);
        resolved!.InstanceType.ShouldBe("Bundle");
    }

    [Fact]
    public void GivenBareHash_WhenResolving_ThenDoesNotCollideWithContainedResourceHavingEmptyId()
    {
        // Arrange
        var element = ToElement(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""contained"": [ { ""resourceType"": ""Practitioner"", ""id"": """" } ]
        }");
        var index = ReferenceIndex.Build(element);

        // Act
        var resolved = index.Resolve("#");

        // Assert
        resolved.ShouldBeSameAs(element);
        resolved!.InstanceType.ShouldBe("Patient");
    }

    [Fact]
    public void GivenUnknownFragment_WhenResolving_ThenStillReturnsNull()
    {
        // Arrange
        var element = ToElement(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""contained"": [ { ""resourceType"": ""Practitioner"", ""id"": ""p1"" } ]
        }");
        var index = ReferenceIndex.Build(element);

        // Act & Assert
        index.Resolve("#unknown").ShouldBeNull();
    }
}
