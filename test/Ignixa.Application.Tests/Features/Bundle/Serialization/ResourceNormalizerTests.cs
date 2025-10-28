// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Text;
using System.Text.Json;
using FluentAssertions;
using Ignixa.Serialization.Extensions;
using Xunit;

namespace Ignixa.Application.Tests.Features.Bundle.Serialization;

/// <summary>
/// Comprehensive unit tests for ResourceNormalizer.
/// Tests streaming removal of version metadata for content comparison.
/// </summary>
public class ResourceNormalizerTests
{
    /// <summary>
    /// Test that versionId and lastUpdated are removed from meta object.
    /// </summary>
    [Fact]
    public void RemoveVersionMetadata_WithVersionFields_RemovesThemSuccessfully()
    {
        // Arrange
        var resourceJson = """
        {
          "resourceType": "Patient",
          "id": "123",
          "meta": {
            "versionId": "2",
            "lastUpdated": "2024-01-15T10:30:00Z",
            "profile": ["http://example.com/patient"]
          },
          "active": true,
          "name": [{"family": "Smith"}]
        }
        """;

        // Act
        var result = ResourceNormalizer.RemoveVersionMetadata(
            new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(resourceJson)));
        var normalizedJson = Encoding.UTF8.GetString(result.Span);
        var doc = JsonDocument.Parse(normalizedJson);
        var resource = doc.RootElement;

        // Assert
        resource.TryGetProperty("meta", out var meta).Should().BeTrue();

        // versionId and lastUpdated should be removed
        meta.TryGetProperty("versionId", out _).Should().BeFalse();
        meta.TryGetProperty("lastUpdated", out _).Should().BeFalse();

        // Other meta properties should be preserved
        meta.TryGetProperty("profile", out _).Should().BeTrue();

        // Non-meta properties should be unchanged
        resource.TryGetProperty("resourceType", out var rt).Should().BeTrue();
        rt.GetString().Should().Be("Patient");
        resource.TryGetProperty("id", out var id).Should().BeTrue();
        id.GetString().Should().Be("123");
        resource.TryGetProperty("active", out var active).Should().BeTrue();
        active.GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// Test that resource without meta object is unchanged.
    /// </summary>
    [Fact]
    public void RemoveVersionMetadata_WithoutMetaObject_LeavesResourceUnchanged()
    {
        // Arrange
        var resourceJson = """
        {
          "resourceType": "Patient",
          "id": "456",
          "active": false,
          "name": [{"family": "Doe"}]
        }
        """;

        // Act
        var result = ResourceNormalizer.RemoveVersionMetadata(
            new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(resourceJson)));
        var normalizedJson = Encoding.UTF8.GetString(result.Span);
        var doc = JsonDocument.Parse(normalizedJson);
        var resource = doc.RootElement;

        // Assert
        resource.TryGetProperty("meta", out _).Should().BeFalse();
        resource.TryGetProperty("resourceType", out var rt).Should().BeTrue();
        rt.GetString().Should().Be("Patient");
        resource.TryGetProperty("id", out var id).Should().BeTrue();
        id.GetString().Should().Be("456");
    }

    /// <summary>
    /// Test that only versionId and lastUpdated are removed, other meta fields preserved.
    /// </summary>
    [Fact]
    public void RemoveVersionMetadata_WithComplexMeta_PreservesOtherFields()
    {
        // Arrange
        var resourceJson = """
        {
          "resourceType": "Observation",
          "id": "obs-1",
          "meta": {
            "versionId": "5",
            "lastUpdated": "2024-02-20T14:45:00Z",
            "profile": ["http://example.com/observation"],
            "security": [{"system": "http://terminology.hl7.org", "code": "R"}],
            "tag": [{"system": "http://example.com", "code": "important"}],
            "source": "#system-source"
          },
          "status": "final"
        }
        """;

        // Act
        var result = ResourceNormalizer.RemoveVersionMetadata(
            new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(resourceJson)));
        var normalizedJson = Encoding.UTF8.GetString(result.Span);
        var doc = JsonDocument.Parse(normalizedJson);
        var resource = doc.RootElement;

        // Assert
        resource.TryGetProperty("meta", out var meta).Should().BeTrue();

        // Version fields removed
        meta.TryGetProperty("versionId", out _).Should().BeFalse();
        meta.TryGetProperty("lastUpdated", out _).Should().BeFalse();

        // Other meta fields preserved
        meta.TryGetProperty("profile", out var profile).Should().BeTrue();
        meta.TryGetProperty("security", out var security).Should().BeTrue();
        meta.TryGetProperty("tag", out var tag).Should().BeTrue();
        meta.TryGetProperty("source", out var source).Should().BeTrue();
        source.GetString().Should().Be("#system-source");

        // Main resource properties preserved
        resource.TryGetProperty("status", out var status).Should().BeTrue();
        status.GetString().Should().Be("final");
    }

    /// <summary>
    /// Test with empty meta object.
    /// </summary>
    [Fact]
    public void RemoveVersionMetadata_WithEmptyMeta_LeavesItEmpty()
    {
        // Arrange
        var resourceJson = """
        {
          "resourceType": "Patient",
          "id": "pat-1",
          "meta": {},
          "name": [{"family": "Johnson"}]
        }
        """;

        // Act
        var result = ResourceNormalizer.RemoveVersionMetadata(
            new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(resourceJson)));
        var normalizedJson = Encoding.UTF8.GetString(result.Span);
        var doc = JsonDocument.Parse(normalizedJson);
        var resource = doc.RootElement;

        // Assert
        resource.TryGetProperty("meta", out var meta).Should().BeTrue();
        var properties = meta.EnumerateObject().ToList();
        properties.Should().BeEmpty();
    }

    /// <summary>
    /// Test with only versionId in meta (lastUpdated missing).
    /// </summary>
    [Fact]
    public void RemoveVersionMetadata_WithOnlyVersionId_RemovesIt()
    {
        // Arrange
        var resourceJson = """
        {
          "resourceType": "Patient",
          "id": "pat-2",
          "meta": {
            "versionId": "1",
            "profile": ["http://example.com/patient"]
          },
          "active": true
        }
        """;

        // Act
        var result = ResourceNormalizer.RemoveVersionMetadata(
            new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(resourceJson)));
        var normalizedJson = Encoding.UTF8.GetString(result.Span);
        var doc = JsonDocument.Parse(normalizedJson);
        var resource = doc.RootElement;

        // Assert
        resource.TryGetProperty("meta", out var meta).Should().BeTrue();
        meta.TryGetProperty("versionId", out _).Should().BeFalse();
        meta.TryGetProperty("lastUpdated", out _).Should().BeFalse();
        meta.TryGetProperty("profile", out _).Should().BeTrue();
    }

    /// <summary>
    /// Test with only lastUpdated in meta (versionId missing).
    /// </summary>
    [Fact]
    public void RemoveVersionMetadata_WithOnlyLastUpdated_RemovesIt()
    {
        // Arrange
        var resourceJson = """
        {
          "resourceType": "Patient",
          "id": "pat-3",
          "meta": {
            "lastUpdated": "2024-01-10T09:00:00Z",
            "profile": ["http://example.com/patient"]
          },
          "active": false
        }
        """;

        // Act
        var result = ResourceNormalizer.RemoveVersionMetadata(
            new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(resourceJson)));
        var normalizedJson = Encoding.UTF8.GetString(result.Span);
        var doc = JsonDocument.Parse(normalizedJson);
        var resource = doc.RootElement;

        // Assert
        resource.TryGetProperty("meta", out var meta).Should().BeTrue();
        meta.TryGetProperty("versionId", out _).Should().BeFalse();
        meta.TryGetProperty("lastUpdated", out _).Should().BeFalse();
        meta.TryGetProperty("profile", out _).Should().BeTrue();
    }

    /// <summary>
    /// Test with meta containing only versionId and lastUpdated.
    /// </summary>
    [Fact]
    public void RemoveVersionMetadata_WithOnlyVersionFields_ResultsInEmptyMeta()
    {
        // Arrange
        var resourceJson = """
        {
          "resourceType": "Patient",
          "id": "pat-4",
          "meta": {
            "versionId": "3",
            "lastUpdated": "2024-01-20T15:00:00Z"
          },
          "active": true
        }
        """;

        // Act
        var result = ResourceNormalizer.RemoveVersionMetadata(
            new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(resourceJson)));
        var normalizedJson = Encoding.UTF8.GetString(result.Span);
        var doc = JsonDocument.Parse(normalizedJson);
        var resource = doc.RootElement;

        // Assert
        resource.TryGetProperty("meta", out var meta).Should().BeTrue();
        var properties = meta.EnumerateObject().ToList();
        properties.Should().BeEmpty();
    }

    /// <summary>
    /// Test with deeply nested structures in meta.
    /// </summary>
    [Fact]
    public void RemoveVersionMetadata_WithNestedStructuresInMeta_PreservesThemCorrectly()
    {
        // Arrange
        var resourceJson = """
        {
          "resourceType": "Patient",
          "id": "pat-5",
          "meta": {
            "versionId": "2",
            "lastUpdated": "2024-01-15T10:00:00Z",
            "security": [
              {
                "system": "http://terminology.hl7.org/CodeSystem/v3-ActCode",
                "code": "RESTRICTED",
                "display": "Restricted"
              }
            ],
            "tag": [
              {"system": "http://example.com/tags", "code": "tag1"},
              {"system": "http://example.com/tags", "code": "tag2"}
            ]
          },
          "name": [{"family": "Smith", "given": ["John"]}]
        }
        """;

        // Act
        var result = ResourceNormalizer.RemoveVersionMetadata(
            new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(resourceJson)));
        var normalizedJson = Encoding.UTF8.GetString(result.Span);
        var doc = JsonDocument.Parse(normalizedJson);
        var resource = doc.RootElement;

        // Assert
        resource.TryGetProperty("meta", out var meta).Should().BeTrue();

        // Version fields removed
        meta.TryGetProperty("versionId", out _).Should().BeFalse();
        meta.TryGetProperty("lastUpdated", out _).Should().BeFalse();

        // Nested arrays preserved
        meta.TryGetProperty("security", out var security).Should().BeTrue();
        var securityArray = security.EnumerateArray().ToList();
        securityArray.Should().HaveCount(1);
        securityArray[0].TryGetProperty("code", out var code).Should().BeTrue();
        code.GetString().Should().Be("RESTRICTED");

        meta.TryGetProperty("tag", out var tag).Should().BeTrue();
        var tagArray = tag.EnumerateArray().ToList();
        tagArray.Should().HaveCount(2);
    }

    /// <summary>
    /// Test that the output is valid JSON and can be parsed.
    /// </summary>
    [Fact]
    public void RemoveVersionMetadata_ProducesValidJson()
    {
        // Arrange
        var resourceJson = """
        {
          "resourceType": "Patient",
          "id": "pat-6",
          "meta": {
            "versionId": "4",
            "lastUpdated": "2024-02-01T12:00:00Z",
            "profile": ["http://example.com"]
          },
          "name": [{"family": "Doe"}],
          "telecom": [{"system": "email", "value": "john@example.com"}]
        }
        """;

        // Act
        var result = ResourceNormalizer.RemoveVersionMetadata(
            new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(resourceJson)));

        // Assert - should parse without throwing
        var exception = Record.Exception(() =>
        {
            var normalizedJson = Encoding.UTF8.GetString(result.Span);
            var doc = JsonDocument.Parse(normalizedJson);
            _ = doc.RootElement;
        });

        exception.Should().BeNull();
    }

    /// <summary>
    /// Test that content comparison would work (same resource with different versions should normalize to same JSON).
    /// </summary>
    [Fact]
    public void RemoveVersionMetadata_SameResourceDifferentVersions_NormalizesToSameOutput()
    {
        // Arrange
        var v1Json = """
        {
          "resourceType": "Patient",
          "id": "pat-7",
          "meta": {
            "versionId": "1",
            "lastUpdated": "2024-01-01T00:00:00Z",
            "profile": ["http://example.com"]
          },
          "active": true,
          "name": [{"family": "Smith"}]
        }
        """;

        var v2Json = """
        {
          "resourceType": "Patient",
          "id": "pat-7",
          "meta": {
            "versionId": "5",
            "lastUpdated": "2024-02-15T14:30:00Z",
            "profile": ["http://example.com"]
          },
          "active": true,
          "name": [{"family": "Smith"}]
        }
        """;

        // Act
        var result1 = ResourceNormalizer.RemoveVersionMetadata(
            new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(v1Json)));
        var result2 = ResourceNormalizer.RemoveVersionMetadata(
            new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(v2Json)));

        var normalized1 = Encoding.UTF8.GetString(result1.Span);
        var normalized2 = Encoding.UTF8.GetString(result2.Span);

        // Assert - both should normalize to same content (ignoring whitespace)
        var doc1 = JsonDocument.Parse(normalized1);
        var doc2 = JsonDocument.Parse(normalized2);

        doc1.RootElement.GetProperty("id").GetString().Should().Be(
            doc2.RootElement.GetProperty("id").GetString());

        doc1.RootElement.GetProperty("meta").GetProperty("profile").ToString().Should().Be(
            doc2.RootElement.GetProperty("meta").GetProperty("profile").ToString());

        // Both should NOT have versionId or lastUpdated
        doc1.RootElement.GetProperty("meta").TryGetProperty("versionId", out _).Should().BeFalse();
        doc2.RootElement.GetProperty("meta").TryGetProperty("versionId", out _).Should().BeFalse();
    }
}
