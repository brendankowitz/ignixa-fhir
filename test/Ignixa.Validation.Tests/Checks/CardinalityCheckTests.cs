// <copyright file="CardinalityCheckTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Tests.TestHelpers;
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
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

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
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

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
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

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
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

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
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

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
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
    }

    [Fact]
    public void GivenAppointmentParticipantWithoutStatus_WhenValidatingInSpecMode_ThenReturnsError()
    {
        // Arrange - Issue #220: Appointment.participant.status is required (1..1)
        var json = JsonNode.Parse("""
        {
          "resourceType": "Appointment",
          "status": "booked",
          "start": "2013-12-10T09:00:00Z",
          "end": "2013-12-10T11:00:00Z",
          "participant": [{
            "actor": { "reference": "Patient/example" }
          }]
        }
        """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var participantElement = sourceNode.ToElement(TestSchemaProvider.GetR4Schema())
            .Children("participant")[0];
        var check = new CardinalityCheck("status", min: 1, max: 1);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var state = new ValidationState();

        // Act
        var result = check.Validate(participantElement, settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "cardinality-violation");
    }

    [Fact]
    public void GivenAppointmentParticipantWithoutStatus_WhenValidatingInCompatibilityMode_ThenReturnsSuccess()
    {
        // Arrange - Issue #220: In Compatibility mode, accept participant without status
        var json = JsonNode.Parse("""
        {
          "resourceType": "Appointment",
          "status": "booked",
          "start": "2013-12-10T09:00:00Z",
          "end": "2013-12-10T11:00:00Z",
          "participant": [{
            "actor": { "reference": "Patient/example" }
          }]
        }
        """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var participantElement = sourceNode.ToElement(TestSchemaProvider.GetR4Schema())
            .Children("participant")[0];
        var check = new CardinalityCheck("status", min: 1, max: 1);
        var settings = new ValidationSettings { Depth = ValidationDepth.Compatibility };
        var state = new ValidationState();

        // Act
        var result = check.Validate(participantElement, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenAppointmentParticipantWithStatus_WhenValidatingInCompatibilityMode_ThenReturnsSuccess()
    {
        // Arrange - Verify normal case still works
        var json = JsonNode.Parse("""
        {
          "resourceType": "Appointment",
          "status": "booked",
          "start": "2013-12-10T09:00:00Z",
          "end": "2013-12-10T11:00:00Z",
          "participant": [{
            "actor": { "reference": "Patient/example" },
            "status": "accepted"
          }]
        }
        """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var participantElement = sourceNode.ToElement(TestSchemaProvider.GetR4Schema())
            .Children("participant")[0];
        var check = new CardinalityCheck("status", min: 1, max: 1);
        var settings = new ValidationSettings { Depth = ValidationDepth.Compatibility };
        var state = new ValidationState();

        // Act
        var result = check.Validate(participantElement, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenFirstLevelRequiredElementMissing_WhenValidatingInCompatibilityMode_ThenStillReturnsError()
    {
        // Arrange - First-level elements should still enforce cardinality even in Compatibility mode
        var json = JsonNode.Parse("""
        {
          "resourceType": "Appointment",
          "participant": [{
            "actor": { "reference": "Patient/example" }
          }]
        }
        """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var appointmentElement = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var check = new CardinalityCheck("status", min: 1, max: 1);
        var settings = new ValidationSettings { Depth = ValidationDepth.Compatibility };
        var state = new ValidationState();

        // Act
        var result = check.Validate(appointmentElement, settings, state);

        // Assert - First level cardinality should still be enforced
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "cardinality-violation");
    }
}
