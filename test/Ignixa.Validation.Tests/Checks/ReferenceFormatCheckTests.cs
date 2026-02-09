// <copyright file="ReferenceFormatCheckTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// Tests for ReferenceFormatCheck.
/// Regression tests for Bug #210-5: ReferenceFormatCheck rejects bare strings and urn:oid references.
/// </summary>
public class ReferenceFormatCheckTests
{
    [Trait("Category", "Regression")]
    [Fact]
    public void GivenUrnOidReference_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Bug #210-5: urn:oid: references are not handled by IsValidReferenceFormat
        // Only urn:uuid: is handled, but urn:oid: is also valid per FHIR spec
        var json = JsonNode.Parse("""
            {
                "resourceType": "Observation",
                "status": "final",
                "code": {"text": "test"},
                "subject": {
                    "reference": "urn:oid:1.2.3.4.5.6.7"
                }
            }
            """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ReferenceFormatCheck("subject");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenUrnUuidReference_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Existing behavior: urn:uuid should pass
        var json = JsonNode.Parse("""
            {
                "resourceType": "Observation",
                "status": "final",
                "code": {"text": "test"},
                "subject": {
                    "reference": "urn:uuid:c757873d-ec9a-4326-a141-556f43239520"
                }
            }
            """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ReferenceFormatCheck("subject");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenRelativeResourceReference_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Relative ResourceType/id should pass
        var json = JsonNode.Parse("""
            {
                "resourceType": "Observation",
                "status": "final",
                "code": {"text": "test"},
                "subject": {
                    "reference": "Patient/123"
                }
            }
            """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ReferenceFormatCheck("subject");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenAbsoluteHttpReference_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Absolute HTTP reference should pass
        var json = JsonNode.Parse("""
            {
                "resourceType": "Observation",
                "status": "final",
                "code": {"text": "test"},
                "subject": {
                    "reference": "http://example.org/fhir/Patient/123"
                }
            }
            """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ReferenceFormatCheck("subject");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenFragmentReference_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Fragment reference should pass
        var json = JsonNode.Parse("""
            {
                "resourceType": "Observation",
                "status": "final",
                "code": {"text": "test"},
                "subject": {
                    "reference": "#contained-patient"
                }
            }
            """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ReferenceFormatCheck("subject");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenBareStringReference_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Bug #210-5: Bare string references like "ijk" should pass validation
        // The Microsoft FHIR Server accepts these at parse time; they may be display-only identifiers
        var json = JsonNode.Parse("""
            {
                "resourceType": "Observation",
                "status": "final",
                "code": {"text": "test"},
                "subject": {
                    "reference": "ijk"
                }
            }
            """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ReferenceFormatCheck("subject");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert - FHIR spec does not reject bare strings at parse time in MS FHIR Server
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenReferenceWithNoReferenceField_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Reference with only display (no .reference) is valid
        var json = JsonNode.Parse("""
            {
                "resourceType": "Observation",
                "status": "final",
                "code": {"text": "test"},
                "subject": {
                    "display": "Some Patient"
                }
            }
            """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ReferenceFormatCheck("subject");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenVersionedRelativeReference_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Versioned relative reference
        var json = JsonNode.Parse("""
            {
                "resourceType": "Observation",
                "status": "final",
                "code": {"text": "test"},
                "subject": {
                    "reference": "Patient/123/_history/1"
                }
            }
            """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ReferenceFormatCheck("subject");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }
}
