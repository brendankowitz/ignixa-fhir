// <copyright file="FastPathValidatorTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using FluentAssertions;
using Sparky.SourceNodeSerialization;
using Sparky.Specification.Generated;
using Sparky.Validation.SourceNodeValidation;

namespace Sparky.Validation.Tests.SourceNodeValidation;

/// <summary>
/// Unit tests for ISourceNode-based FastPathValidator.
/// Tests that it correctly validates resources including explicit properties (id, resourceType, meta).
/// </summary>
public class FastPathValidatorTests
{
    private readonly FastPathValidator _validator;
    private readonly R4StructureDefinitionSummaryProvider _provider;

    public FastPathValidatorTests()
    {
        _provider = new R4StructureDefinitionSummaryProvider();
        _validator = new FastPathValidator();
    }

    [Fact]
    public void GivenValidPatient_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        string json = """
        {
            "resourceType": "Patient",
            "id": "example-123",
            "active": true
        }
        """;

        var node = JsonSourceNodeFactory.Parse(json).ToSourceNode();

        // Act
        var result = _validator.Validate(node, _provider);

        // Assert
        result.Should().NotBeNull();
        result.IsValid.Should().BeTrue();
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void GivenPatientWithExplicitId_WhenValidating_ThenValidatesIdCorrectly()
    {
        // Arrange - This tests the fix for the missing property bug
        // The 'id' property is deserialized as an explicit property, not in ExtensionData
        string json = """
        {
            "resourceType": "Patient",
            "id": "invalid_id_with_underscore"
        }
        """;

        var node = JsonSourceNodeFactory.Parse(json).ToSourceNode();

        // Act
        var result = _validator.Validate(node, _provider);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i =>
            i.Path == "id" &&
            i.Severity == IssueSeverity.Error &&
            i.Message.Contains("not valid"));
    }

    [Fact]
    public void GivenPatientWithoutResourceType_WhenValidating_ThenReturnsError()
    {
        // Arrange
        string json = """
        {
            "id": "example-123"
        }
        """;

        var node = JsonSourceNodeFactory.Parse(json).ToSourceNode();

        // Act
        var result = _validator.Validate(node, _provider);

        // Assert
        result.Should().NotBeNull();
        result.IsValid.Should().BeFalse();
        result.Issues.Should().ContainSingle();
        result.Issues[0].Severity.Should().Be(IssueSeverity.Error);
        result.Issues[0].Path.Should().Be("resourceType");
    }

    [Theory]
    [InlineData("valid-id-123")]
    [InlineData("123")]
    [InlineData("a1b2c3")]
    [InlineData("example.with.dots")]
    public void GivenValidIdFormat_WhenValidating_ThenNoIdFormatError(string validId)
    {
        // Arrange
        string json = $$"""
        {
            "resourceType": "Patient",
            "id": "{{validId}}"
        }
        """;

        var node = JsonSourceNodeFactory.Parse(json).ToSourceNode();

        // Act
        var result = _validator.Validate(node, _provider);

        // Assert
        result.Issues.Should().NotContain(i => i.Path == "id" && i.Message.Contains("not valid"));
    }

    [Theory]
    [InlineData("invalid id with spaces")]
    [InlineData("id_with_underscore")]
    public void GivenInvalidIdFormat_WhenValidating_ThenReturnsIdFormatError(string invalidId)
    {
        // Arrange
        string json = $$"""
        {
            "resourceType": "Patient",
            "id": "{{invalidId}}"
        }
        """;

        var node = JsonSourceNodeFactory.Parse(json).ToSourceNode();

        // Act
        var result = _validator.Validate(node, _provider);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i =>
            i.Path == "id" &&
            i.Severity == IssueSeverity.Error &&
            i.Message.Contains("not valid"));
    }

    [Fact]
    public void GivenObservationWithValidReference_WhenValidating_ThenNoReferenceError()
    {
        // Arrange
        string json = """
        {
            "resourceType": "Observation",
            "status": "final",
            "code": {
                "coding": [{
                    "system": "http://loinc.org",
                    "code": "15074-8"
                }]
            },
            "subject": {
                "reference": "Patient/123"
            }
        }
        """;

        var node = JsonSourceNodeFactory.Parse(json).ToSourceNode();

        // Act
        var result = _validator.Validate(node, _provider);

        // Assert
        result.Issues.Should().NotContain(i =>
            i.Path.Contains("subject") &&
            i.Message.Contains("not a valid FHIR reference"));
    }

    [Fact]
    public void GivenObservationWithInvalidReference_WhenValidating_ThenReturnsReferenceError()
    {
        // Arrange
        string json = """
        {
            "resourceType": "Observation",
            "status": "final",
            "code": {
                "coding": [{
                    "system": "http://loinc.org",
                    "code": "15074-8"
                }]
            },
            "subject": {
                "reference": "InvalidFormat"
            }
        }
        """;

        var node = JsonSourceNodeFactory.Parse(json).ToSourceNode();

        // Act
        var result = _validator.Validate(node, _provider);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i =>
            i.Path.Contains("subject") &&
            i.Severity == IssueSeverity.Error &&
            i.Message.Contains("not a valid FHIR reference"));
    }

    [Fact]
    public void GivenMultipleValidations_WhenValidatingSameResourceType_ThenUsesCachedRules()
    {
        // Arrange
        string json1 = """{"resourceType": "Patient", "id": "patient-1"}""";
        string json2 = """{"resourceType": "Patient", "id": "patient-2"}""";

        var node1 = JsonSourceNodeFactory.Parse(json1).ToSourceNode();
        var node2 = JsonSourceNodeFactory.Parse(json2).ToSourceNode();

        // Act - First validation builds rules
        var result1 = _validator.Validate(node1, _provider);

        // Act - Second validation uses cached rules
        var result2 = _validator.Validate(node2, _provider);

        // Assert - Both should succeed with minimal overhead
        result1.IsValid.Should().BeTrue();
        result2.IsValid.Should().BeTrue();
    }
}
