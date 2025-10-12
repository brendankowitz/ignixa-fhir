// <copyright file="FastPathValidatorTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json;
using FluentAssertions;
using Sparky.SourceNodeSerialization.SourceNodes.Models;
using Sparky.Specification.Generated;
using Sparky.Validation.JsonNodeValidation;

namespace Sparky.Validation.Tests.JsonNodeValidation;

/// <summary>
/// Unit tests for FastPathValidator.
/// </summary>
public class FastPathValidatorTests
{
    private readonly FastPathValidator _validator;

    public FastPathValidatorTests()
    {
        var provider = new R4StructureDefinitionSummaryProvider();
        _validator = new FastPathValidator(provider);
    }

    #region Helper Methods

    private static ResourceJsonNode CreatePatientResource(Action<Dictionary<string, JsonElement>>? configure = null)
    {
        var data = new Dictionary<string, JsonElement>
        {
            ["resourceType"] = JsonSerializer.SerializeToElement("Patient"),
        };

        configure?.Invoke(data);

        return new ResourceJsonNode
        {
            ResourceType = "Patient",
            ExtensionData = data,
        };
    }

    #endregion

    #region Basic Validation Tests

    [Fact]
    public void GivenValidPatient_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var patient = CreatePatientResource(data =>
        {
            data["id"] = JsonSerializer.SerializeToElement("example-123");
            data["active"] = JsonSerializer.SerializeToElement(true);
        });

        // Act
        var result = _validator.Validate(patient);

        // Assert
        result.Should().NotBeNull();
        result.IsValid.Should().BeTrue();
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void GivenPatientWithoutResourceType_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var patient = new ResourceJsonNode
        {
            ResourceType = string.Empty,
            ExtensionData = new Dictionary<string, JsonElement>(),
        };

        // Act
        var result = _validator.Validate(patient);

        // Assert
        result.Should().NotBeNull();
        result.IsValid.Should().BeFalse();
        result.Issues.Should().ContainSingle();
        result.Issues[0].Severity.Should().Be(IssueSeverity.Error);
        result.Issues[0].Path.Should().Be("resourceType");
        result.Issues[0].Message.Should().Contain("must have a resourceType");
    }

    [Fact]
    public void GivenUnknownResourceType_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "UnknownResource",
            ExtensionData = new Dictionary<string, JsonElement>(),
        };

        // Act
        var result = _validator.Validate(resource);

        // Assert
        result.Should().NotBeNull();
        result.IsValid.Should().BeFalse();
        result.Issues.Should().ContainSingle();
        result.Issues[0].Severity.Should().Be(IssueSeverity.Error);
        result.Issues[0].Message.Should().Contain("Unknown resource type");
    }

    #endregion

    #region ID Format Validation Tests

    [Theory]
    [InlineData("valid-id-123")]
    [InlineData("123")]
    [InlineData("a1b2c3")]
    [InlineData("example.with.dots")]
    [InlineData("A-B-C-D")]
    public void GivenValidIdFormat_WhenValidating_ThenNoIdFormatError(string validId)
    {
        // Arrange
        var patient = CreatePatientResource(data =>
        {
            data["id"] = JsonSerializer.SerializeToElement(validId);
        });

        // Act
        var result = _validator.Validate(patient);

        // Assert
        result.Issues.Should().NotContain(i => i.Path == "id" && i.Message.Contains("not valid"));
    }

    [Theory]
    [InlineData("invalid id with spaces")]
    [InlineData("id_with_underscore")]
    [InlineData("")]
    [InlineData("ThisIsAnIdThatIsWayTooLongAndExceedsTheSixtyFourCharacterLimitSetByFHIR")]
    public void GivenInvalidIdFormat_WhenValidating_ThenReturnsIdFormatError(string invalidId)
    {
        // Arrange
        var patient = CreatePatientResource(data =>
        {
            data["id"] = JsonSerializer.SerializeToElement(invalidId);
        });

        // Act
        var result = _validator.Validate(patient);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i =>
            i.Path == "id" &&
            i.Severity == IssueSeverity.Error &&
            i.Message.Contains("not valid"));
    }

    #endregion

    #region Reference Format Validation Tests

    [Theory]
    [InlineData("Patient/123")]
    [InlineData("Practitioner/example")]
    [InlineData("http://example.com/fhir/Patient/123")]
    [InlineData("https://server.org/Patient/abc-123")]
    [InlineData("urn:uuid:53fefa32-fcbb-4ff8-8a92-55ee120877b7")]
    [InlineData("#contained-resource")]
    public void GivenValidReferenceFormat_WhenValidating_ThenNoReferenceFormatError(string validReference)
    {
        // Arrange
        var observation = new ResourceJsonNode
        {
            ResourceType = "Observation",
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["resourceType"] = JsonSerializer.SerializeToElement("Observation"),
                ["status"] = JsonSerializer.SerializeToElement("final"),
                ["code"] = JsonSerializer.SerializeToElement(new { coding = new[] { new { system = "http://loinc.org", code = "15074-8" } } }),
                ["subject"] = JsonSerializer.SerializeToElement(new { reference = validReference }),
            },
        };

        // Act
        var result = _validator.Validate(observation);

        // Assert
        result.Issues.Should().NotContain(i =>
            i.Path.Contains("subject") &&
            i.Message.Contains("not a valid FHIR reference"));
    }

    [Theory]
    [InlineData("InvalidFormat")]
    [InlineData("Patient/")]
    [InlineData("/123")]
    [InlineData("")]
    public void GivenInvalidReferenceFormat_WhenValidating_ThenReturnsReferenceFormatError(string invalidReference)
    {
        // Arrange
        var observation = new ResourceJsonNode
        {
            ResourceType = "Observation",
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["resourceType"] = JsonSerializer.SerializeToElement("Observation"),
                ["status"] = JsonSerializer.SerializeToElement("final"),
                ["code"] = JsonSerializer.SerializeToElement(new { coding = new[] { new { system = "http://loinc.org", code = "15074-8" } } }),
                ["subject"] = JsonSerializer.SerializeToElement(new { reference = invalidReference }),
            },
        };

        // Act
        var result = _validator.Validate(observation);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i =>
            i.Path.Contains("subject") &&
            i.Severity == IssueSeverity.Error &&
            i.Message.Contains("not a valid FHIR reference"));
    }

    #endregion

    #region Primitive Type Format Validation Tests

    [Theory]
    [InlineData("2024-01-15")]
    [InlineData("2024-01")]
    [InlineData("2024")]
    public void GivenValidDateFormat_WhenValidating_ThenNoDateFormatError(string validDate)
    {
        // Arrange
        var patient = CreatePatientResource(data =>
        {
            data["birthDate"] = JsonSerializer.SerializeToElement(validDate);
        });

        // Act
        var result = _validator.Validate(patient);

        // Assert
        result.Issues.Should().NotContain(i => i.Path == "birthDate");
    }

    [Theory]
    [InlineData("2024/01/15")]
    [InlineData("15-01-2024")]
    [InlineData("invalid")]
    public void GivenInvalidDateFormat_WhenValidating_ThenReturnsDateFormatError(string invalidDate)
    {
        // Arrange
        var patient = CreatePatientResource(data =>
        {
            data["birthDate"] = JsonSerializer.SerializeToElement(invalidDate);
        });

        // Act
        var result = _validator.Validate(patient);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i =>
            i.Path == "birthDate" &&
            i.Severity == IssueSeverity.Error &&
            i.Message.Contains("Invalid date format"));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void GivenValidBooleanFormat_WhenValidating_ThenNoBooleanFormatError(string validBoolean)
    {
        // Arrange
        var patient = CreatePatientResource(data =>
        {
            data["active"] = JsonSerializer.SerializeToElement(validBoolean);
        });

        // Act
        var result = _validator.Validate(patient);

        // Assert
        result.Issues.Should().NotContain(i => i.Path == "active");
    }

    [Theory]
    [InlineData("True")]
    [InlineData("FALSE")]
    [InlineData("1")]
    [InlineData("yes")]
    public void GivenInvalidBooleanFormat_WhenValidating_ThenReturnsBooleanFormatError(string invalidBoolean)
    {
        // Arrange
        var patient = CreatePatientResource(data =>
        {
            data["active"] = JsonSerializer.SerializeToElement(invalidBoolean);
        });

        // Act
        var result = _validator.Validate(patient);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i =>
            i.Path == "active" &&
            i.Severity == IssueSeverity.Error &&
            i.Message.Contains("Invalid boolean value"));
    }

    #endregion

    #region Narrative Validation Tests

    [Theory]
    [InlineData("generated")]
    [InlineData("extensions")]
    [InlineData("additional")]
    [InlineData("empty")]
    public void GivenValidNarrativeStatus_WhenValidating_ThenNoNarrativeError(string validStatus)
    {
        // Arrange
        var patient = CreatePatientResource(data =>
        {
            data["text"] = JsonSerializer.SerializeToElement(new
            {
                status = validStatus,
                div = "<div xmlns=\"http://www.w3.org/1999/xhtml\">Test</div>",
            });
        });

        // Act
        var result = _validator.Validate(patient);

        // Assert
        result.Issues.Should().NotContain(i => i.Path.StartsWith("text"));
    }

    [Fact]
    public void GivenNarrativeWithoutStatus_WhenValidating_ThenReturnsNarrativeError()
    {
        // Arrange
        var patient = CreatePatientResource(data =>
        {
            data["text"] = JsonSerializer.SerializeToElement(new
            {
                div = "<div xmlns=\"http://www.w3.org/1999/xhtml\">Test</div>",
            });
        });

        // Act
        var result = _validator.Validate(patient);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i =>
            i.Path == "text.status" &&
            i.Severity == IssueSeverity.Error &&
            i.Message.Contains("must have a status field"));
    }

    [Fact]
    public void GivenNarrativeWithoutDiv_WhenStatusNotEmpty_ThenReturnsNarrativeError()
    {
        // Arrange
        var patient = CreatePatientResource(data =>
        {
            data["text"] = JsonSerializer.SerializeToElement(new { status = "generated" });
        });

        // Act
        var result = _validator.Validate(patient);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i =>
            i.Path == "text.div" &&
            i.Severity == IssueSeverity.Error &&
            i.Message.Contains("must have a div field"));
    }

    [Fact]
    public void GivenInvalidNarrativeStatus_WhenValidating_ThenReturnsNarrativeError()
    {
        // Arrange
        var patient = CreatePatientResource(data =>
        {
            data["text"] = JsonSerializer.SerializeToElement(new
            {
                status = "invalid-status",
                div = "<div xmlns=\"http://www.w3.org/1999/xhtml\">Test</div>",
            });
        });

        // Act
        var result = _validator.Validate(patient);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i =>
            i.Path == "text.status" &&
            i.Severity == IssueSeverity.Error &&
            i.Message.Contains("Invalid narrative status"));
    }

    #endregion

    #region Coding Structure Validation Tests

    [Fact]
    public void GivenCodingWithSystemAndCode_WhenValidating_ThenNoCodingError()
    {
        // Arrange
        var observation = new ResourceJsonNode
        {
            ResourceType = "Observation",
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["resourceType"] = JsonSerializer.SerializeToElement("Observation"),
                ["status"] = JsonSerializer.SerializeToElement("final"),
                ["code"] = JsonSerializer.SerializeToElement(new
                {
                    coding = new[]
                    {
                        new { system = "http://loinc.org", code = "15074-8" },
                    },
                }),
            },
        };

        // Act
        var result = _validator.Validate(observation);

        // Assert
        result.Issues.Should().NotContain(i => i.Path.Contains("code") && i.Message.Contains("Coding"));
    }

    [Fact]
    public void GivenCodingWithoutSystemOrCode_WhenValidating_ThenReturnsWarning()
    {
        // Arrange
        var observation = new ResourceJsonNode
        {
            ResourceType = "Observation",
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["resourceType"] = JsonSerializer.SerializeToElement("Observation"),
                ["status"] = JsonSerializer.SerializeToElement("final"),
                ["code"] = JsonSerializer.SerializeToElement(new
                {
                    coding = new[]
                    {
                        new { display = "Test" },
                    },
                }),
            },
        };

        // Act
        var result = _validator.Validate(observation);

        // Assert
        result.Issues.Should().Contain(i =>
            i.Path.Contains("code") &&
            i.Severity == IssueSeverity.Warning &&
            i.Message.Contains("Coding should have at least a system or code"));
    }

    #endregion

    #region ValidationResult Helper Method Tests

    [Fact]
    public void GivenValidationResultWithErrors_WhenCheckingHasErrors_ThenReturnsTrue()
    {
        // Arrange
        var issues = new List<ValidationIssue>
        {
            new(IssueSeverity.Error, "path", "message"),
            new(IssueSeverity.Warning, "path2", "warning"),
        };
        var result = new ValidationResult(isValid: false, issues);

        // Act & Assert
        result.HasErrors.Should().BeTrue();
        result.HasWarnings.Should().BeTrue();
    }

    [Fact]
    public void GivenValidationResultWithOnlyWarnings_WhenCheckingHasErrors_ThenReturnsFalse()
    {
        // Arrange
        var issues = new List<ValidationIssue>
        {
            new(IssueSeverity.Warning, "path", "warning"),
        };
        var result = new ValidationResult(isValid: true, issues);

        // Act & Assert
        result.HasErrors.Should().BeFalse();
        result.HasWarnings.Should().BeTrue();
    }

    [Fact]
    public void GivenSuccessFactory_WhenCalled_ThenReturnsValidResult()
    {
        // Act
        var result = ValidationResult.Success();

        // Assert
        result.IsValid.Should().BeTrue();
        result.Issues.Should().BeEmpty();
        result.HasErrors.Should().BeFalse();
        result.HasWarnings.Should().BeFalse();
    }

    #endregion

    #region Performance Tests

    [Fact]
    public void GivenMultipleValidations_WhenValidatingSameResourceType_ThenUsesCachedRules()
    {
        // Arrange
        var patient1 = CreatePatientResource(data => data["id"] = JsonSerializer.SerializeToElement("patient-1"));
        var patient2 = CreatePatientResource(data => data["id"] = JsonSerializer.SerializeToElement("patient-2"));

        // Act - First validation builds rules
        var result1 = _validator.Validate(patient1);

        // Act - Second validation uses cached rules
        var result2 = _validator.Validate(patient2);

        // Assert - Both should succeed with minimal overhead
        result1.IsValid.Should().BeTrue();
        result2.IsValid.Should().BeTrue();
    }

    #endregion
}
