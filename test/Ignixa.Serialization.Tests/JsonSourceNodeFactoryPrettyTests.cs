// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using FluentAssertions;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Xunit;

namespace Ignixa.Serialization.Tests;

/// <summary>
/// Tests for JsonSourceNodeFactory pretty printing functionality.
/// Validates that the _pretty parameter correctly formats JSON output.
/// </summary>
public class JsonSourceNodeFactoryPrettyTests
{
    [Fact]
    public void SerializeToString_WithPrettyFalse_ReturnsMinifiedJson()
    {
        // Arrange
        var json = """
        {
            "resourceType": "Patient",
            "id": "example",
            "name": [
                {
                    "family": "Smith",
                    "given": ["John"]
                }
            ]
        }
        """;
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);

        // Act
        var result = patient.SerializeToString(pretty: false);

        // Assert
        result.Should().NotContain("\n");
        result.Should().NotContain("  ");
        result.Should().Contain("\"resourceType\":\"Patient\"");
    }

    [Fact]
    public void SerializeToString_WithPrettyTrue_ReturnsFormattedJson()
    {
        // Arrange
        var json = """{"resourceType":"Patient","id":"example","name":[{"family":"Smith","given":["John"]}]}""";
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);

        // Act
        var result = patient.SerializeToString(pretty: true);

        // Assert
        result.Should().Contain("\n");
        result.Should().Contain("  ");
        result.Should().MatchRegex("\"resourceType\"\\s*:\\s*\"Patient\"");
    }

    [Fact]
    public void SerializeToString_DefaultParameter_ReturnsMinifiedJson()
    {
        // Arrange
        var json = """
        {
            "resourceType": "Patient",
            "id": "example"
        }
        """;
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);

        // Act - not specifying pretty parameter, should default to false
        var result = patient.SerializeToString();

        // Assert
        result.Should().NotContain("\n");
        result.Should().NotContain("  ");
    }

    [Fact]
    public void SerializeToStream_WithPrettyFalse_WritesMinifiedJson()
    {
        // Arrange
        var json = """
        {
            "resourceType": "Patient",
            "id": "example",
            "name": [
                {
                    "family": "Smith"
                }
            ]
        }
        """;
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);
        var stream = new MemoryStream();

        // Act
        patient.SerializeToStream(stream, pretty: false);

        // Assert
        stream.Position = 0;
        var result = Encoding.UTF8.GetString(stream.ToArray());
        result.Should().NotContain("\n");
        result.Should().NotContain("  ");
    }

    [Fact]
    public void SerializeToStream_WithPrettyTrue_WritesFormattedJson()
    {
        // Arrange
        var json = """{"resourceType":"Patient","id":"example"}""";
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);
        var stream = new MemoryStream();

        // Act
        patient.SerializeToStream(stream, pretty: true);

        // Assert
        stream.Position = 0;
        var result = Encoding.UTF8.GetString(stream.ToArray());
        result.Should().Contain("\n");
        result.Should().Contain("  ");
    }

    [Fact]
    public void SerializeToBytes_WithPrettyFalse_ReturnsMinifiedJson()
    {
        // Arrange
        var json = """
        {
            "resourceType": "Patient",
            "id": "example"
        }
        """;
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);

        // Act
        var bytes = patient.SerializeToBytes(pretty: false);

        // Assert
        var result = Encoding.UTF8.GetString(bytes.ToArray());
        result.Should().NotContain("\n");
        result.Should().NotContain("  ");
    }

    [Fact]
    public void SerializeToBytes_WithPrettyTrue_ReturnsFormattedJson()
    {
        // Arrange
        var json = """{"resourceType":"Patient","id":"example"}""";
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);

        // Act
        var bytes = patient.SerializeToBytes(pretty: true);

        // Assert
        var result = Encoding.UTF8.GetString(bytes.ToArray());
        result.Should().Contain("\n");
        result.Should().Contain("  ");
    }

    [Fact]
    public void SerializeToString_PrettyTrue_MaintainsDataIntegrity()
    {
        // Arrange
        var json = """
        {
            "resourceType": "Patient",
            "id": "complex-example",
            "active": true,
            "name": [
                {
                    "use": "official",
                    "family": "Smith",
                    "given": ["John", "Jacob"]
                }
            ],
            "birthDate": "1990-01-01"
        }
        """;
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);

        // Act
        var prettyResult = patient.SerializeToString(pretty: true);
        var minifiedResult = patient.SerializeToString(pretty: false);

        // Re-parse both to ensure they represent the same data
        var reparsedPretty = JsonSourceNodeFactory.Parse<ResourceJsonNode>(prettyResult);
        var reparsedMinified = JsonSourceNodeFactory.Parse<ResourceJsonNode>(minifiedResult);

        // Assert - both should have the same data
        reparsedPretty.ResourceType.Should().Be(reparsedMinified.ResourceType);
        reparsedPretty.Id.Should().Be(reparsedMinified.Id);
    }
}
