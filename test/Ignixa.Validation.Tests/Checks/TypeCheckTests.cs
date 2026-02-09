// <copyright file="TypeCheckTests.cs" company="Microsoft Corporation">
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
/// Tests for TypeCheck.
/// </summary>
public class TypeCheckTests
{
    [Fact]
    public void GivenCorrectStringType_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"gender\":\"male\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("gender", "string");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenCorrectBooleanType_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"active\":true}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("active", "boolean");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenCorrectIntegerType_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"multipleBirthInteger\":2}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("multipleBirthInteger", "integer");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenDecimalAsInteger_WhenValidating_ThenReturnsError()
    {
        // Arrange - Note: JSON numbers are interpreted as strings by ISourceNode.Text
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"multipleBirthInteger\":2.5}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("multipleBirthInteger", "integer");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
    }

    [Fact]
    public void GivenCorrectDecimalType_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Observation\",\"value\":98.6}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("value", "decimal");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenCorrectDateFormat_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"birthDate\":\"1990-01-15\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("birthDate", "date");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenPartialDateFormat_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"birthDate\":\"1990\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("birthDate", "date");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenIncorrectDateFormat_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"birthDate\":\"not-a-date\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("birthDate", "date");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
    }

    [Fact]
    public void GivenMissingField_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"other\":\"value\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("birthDate", "date");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid); // Missing fields handled by RequiredFieldCheck
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenValidInstantWithZUtc_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"AuditEvent\",\"recorded\":\"2021-05-28T00:00:00.000Z\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("recorded", "instant");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenValidInstantWithPositiveOffset_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"AuditEvent\",\"recorded\":\"2021-05-28T13:28:17.239+02:00\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("recorded", "instant");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenValidInstantWithNegativeOffset_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"AuditEvent\",\"recorded\":\"2021-05-28T00:00:00-05:00\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("recorded", "instant");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenInstantWithoutTimezone_WhenValidating_ThenReturnsError()
    {
        // Arrange - This is the error from validate.fhir.org (line 374 of SearchDataBatch.json)
        var json = JsonNode.Parse("{\"resourceType\":\"AuditEvent\",\"recorded\":\"2021-05-28T00:00:00.000\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("recorded", "instant");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains("instant", result.Issues[0].Message);
    }

    [Fact]
    public void GivenValidAbsoluteCanonical_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"StructureDefinition\",\"url\":\"http://hl7.org/fhir/StructureDefinition/Patient|4.0.1\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("url", "canonical");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }


    #region JSON Type Mismatch Tests

    [Fact]
    public void GivenIntegerValueForStringField_WhenValidating_ThenReturnsError()
    {
        // Arrange - Integer 2 where string expected
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"name\":[{\"family\":2}]}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var nameElement = element.Children("name")[0];
        var check = new TypeCheck("family", "string");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(nameElement, settings, state);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldHaveSingleItem();
        result.Issues[0].Message.ShouldContain("type");
    }

    [Fact]
    public void GivenBooleanValueForStringField_WhenValidating_ThenReturnsError()
    {
        // Arrange - Boolean where string expected
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"name\":[{\"family\":false}]}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var nameElement = element.Children("name")[0];
        var check = new TypeCheck("family", "string");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(nameElement, settings, state);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldHaveSingleItem();
        result.Issues[0].Message.ShouldContain("type");
    }

    [Fact]
    public void GivenNumberValueForBooleanField_WhenValidating_ThenReturnsError()
    {
        // Arrange - Number where boolean expected
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"active\":1}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("active", "boolean");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldHaveSingleItem();
        result.Issues[0].Message.ShouldContain("type");
    }

    [Fact]
    public void GivenObjectValueForStringField_WhenValidating_ThenReturnsError()
    {
        // Arrange - Object where string expected
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"name\":[{\"family\":{\"nested\":\"value\"}}]}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var nameElement = element.Children("name")[0];
        var check = new TypeCheck("family", "string");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(nameElement, settings, state);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldHaveSingleItem();
        result.Issues[0].Message.ShouldContain("type");
    }

    [Fact]
    public void GivenStringValueForIntegerField_WhenValidating_ThenReturnsError()
    {
        // Arrange - String where integer expected
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"multipleBirthInteger\":\"two\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("multipleBirthInteger", "integer");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldHaveSingleItem();
    }

    [Fact]
    public void GivenStringValueForBooleanField_WhenValidating_ThenReturnsError()
    {
        // Arrange - String where boolean expected
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"active\":\"yes\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("active", "boolean");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldHaveSingleItem();
    }

    [Fact]
    public void GivenValidStringValue_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Valid string value
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"name\":[{\"family\":\"Smith\"}]}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var nameElement = element.Children("name")[0];
        var check = new TypeCheck("family", "string");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(nameElement, settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenValidIntegerValue_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Valid integer value
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"multipleBirthInteger\":3}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("multipleBirthInteger", "integer");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    #endregion

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenPrimitiveWithExtensionOnly_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - _birthDate with extension but no birthDate value
        // Bug #210-1: TypeCheck gets Meta<JsonNode>() which returns JSON Object from shadow property,
        // IsValidJsonType rejects Object for date type before checking HasPrimitiveValue
        var json = JsonNode.Parse("""{"resourceType":"Patient","_birthDate":{"extension":[{"url":"http://example.org","valueCode":"unknown"}]}}""");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("birthDate", "date");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert - element has no primitive value, so type check should pass (not fail on JSON Object kind)
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenPrimitiveWithValueAndExtension_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - both birthDate and _birthDate present
        var json = JsonNode.Parse("""{"resourceType":"Patient","birthDate":"1970-01-01","_birthDate":{"extension":[{"url":"http://example.org","valueString":"approximate"}]}}""");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("birthDate", "date");
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
    public void GivenNullPrimitiveWithExtension_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - birthDate is explicitly null but _birthDate has extension
        // FHIR allows: {"birthDate": null, "_birthDate": {"extension": [...]}}
        var json = JsonNode.Parse("""{"resourceType":"Patient","birthDate":null,"_birthDate":{"extension":[{"url":"http://example.org","valueCode":"unknown"}]}}""");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("birthDate", "date");
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
    public void GivenBooleanWithExtension_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - boolean primitive with shadow extension property
        var json = JsonNode.Parse("""{"resourceType":"Patient","active":true,"_active":{"extension":[{"url":"http://example.org","valueString":"confirmed"}]}}""");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("active", "boolean");
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
    public void GivenBooleanExtensionOnly_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - _active with extension but no active value
        var json = JsonNode.Parse("""{"resourceType":"Patient","_active":{"extension":[{"url":"http://example.org","valueString":"unknown"}]}}""");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("active", "boolean");
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
    public void GivenStringWithExtension_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - string primitive with shadow extension (nested in HumanName)
        var json = JsonNode.Parse("""{"resourceType":"Patient","name":[{"family":"Smith","_family":{"extension":[{"url":"http://example.org","valueString":"maiden"}]}}]}""");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var nameElement = element.Children("name")[0];
        var check = new TypeCheck("family", "string");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(nameElement, settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenRelativeCanonicalReference_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Bug #210-3: Canonical validation uses Uri.TryCreate(UriKind.Absolute) which rejects relative refs
        // Relative canonical references are valid per FHIR spec, especially in Bundle context
        var json = JsonNode.Parse("""{"resourceType":"Library","url":"Library/suiciderisk-orderset-logic"}""");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("url", "canonical");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Trait("Category", "Regression")]
    [Theory]
    [InlineData("ActivityDefinition/referralPrimaryCareMentalHealth-initial")]
    [InlineData("CapabilityStatement/example")]
    [InlineData("Library/library-cms146-example")]
    [InlineData("StructureDefinition/Patient")]
    [InlineData("ValueSet/my-valueset|1.0.0")]
    public void GivenRelativeCanonicalFromTestData_WhenValidating_ThenReturnsSuccess(string canonicalValue)
    {
        // Arrange - Bug #210-3: These relative canonical values appear in official FHIR test data
        var json = JsonNode.Parse($$"""{"resourceType":"Library","url":"{{canonicalValue}}"}""");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("url", "canonical");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeTrue($"Canonical '{canonicalValue}' should be valid");
        result.Issues.ShouldBeEmpty();
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenRelativeCanonicalWithVersion_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Relative canonical with version fragment
        var json = JsonNode.Parse("""{"resourceType":"Library","url":"Library/my-lib|1.0"}""");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("url", "canonical");
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
    public void GivenAbsoluteCanonical_WhenValidating_ThenStillReturnsSuccess()
    {
        // Arrange - Existing behavior: absolute canonical should still pass
        var json = JsonNode.Parse("""{"resourceType":"Library","url":"http://example.org/Library/my-lib"}""");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("url", "canonical");
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
    public void GivenDateWithTimezoneNoTime_WhenValidatingAtSpecDepth_ThenReturnsSuccess()
    {
        // Arrange - Bug #210-4: Date with timezone but no time component
        // Not valid per R4 spec, but accepted at Spec/Compatibility depth for real-world data compatibility
        // R5 spec explicitly allows this format
        var json = JsonNode.Parse("""{"resourceType":"Condition","recordedDate":"2021-10-13+02:00"}""");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("recordedDate", "dateTime");
        var settings = new ValidationSettings { Depth = Ignixa.Validation.Abstractions.ValidationDepth.Spec };
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Trait("Category", "Regression")]
    [Theory]
    [InlineData("2021-10-13-05:00")]
    [InlineData("2021-01-01Z")]
    public void GivenDateWithTimezoneVariants_WhenValidatingAtSpecDepth_ThenReturnsSuccess(string dateTimeValue)
    {
        // Arrange - Accepted at Spec/Compatibility depth for real-world FHIR data compatibility
        var json = JsonNode.Parse($$"""{"resourceType":"Condition","recordedDate":"{{dateTimeValue}}"}""");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("recordedDate", "dateTime");
        var settings = new ValidationSettings { Depth = Ignixa.Validation.Abstractions.ValidationDepth.Spec };
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeTrue($"dateTime '{dateTimeValue}' should be valid at Spec depth");
        result.Issues.ShouldBeEmpty();
    }

    [Trait("Category", "Regression")]
    [Theory]
    [InlineData("2021-10-13+02:00")]
    [InlineData("2021-10-13-05:00")]
    [InlineData("2021-01-01Z")]
    public void GivenDateWithTimezoneNoTime_WhenValidatingAtFullDepth_ThenReturnsError(string dateTimeValue)
    {
        // Arrange - R4 spec requires timezone only after full time component
        // Full depth enforces strict R4 compliance
        var json = JsonNode.Parse($$"""{"resourceType":"Condition","recordedDate":"{{dateTimeValue}}"}""");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("recordedDate", "dateTime");
        var settings = new ValidationSettings { Depth = Ignixa.Validation.Abstractions.ValidationDepth.Full };
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeFalse($"dateTime '{dateTimeValue}' should fail at Full depth (R4 strict)");
        result.Issues.ShouldHaveSingleItem();
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenDateWithTimezoneNoTime_WhenValidatingAtCompatibilityDepth_ThenReturnsSuccess()
    {
        // Arrange - Compatibility depth is deliberately permissive
        var json = JsonNode.Parse("""{"resourceType":"Condition","recordedDate":"2021-10-13+02:00"}""");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("recordedDate", "dateTime");
        var settings = new ValidationSettings { Depth = Ignixa.Validation.Abstractions.ValidationDepth.Compatibility };
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenTimeWithoutTimezone_WhenValidatingAtSpecDepth_ThenReturnsSuccess()
    {
        // Arrange - Permissive pattern allows time without timezone
        var json = JsonNode.Parse("""{"resourceType":"Condition","recordedDate":"2023-01-01T12:00:00"}""");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("recordedDate", "dateTime");
        var settings = new ValidationSettings { Depth = Ignixa.Validation.Abstractions.ValidationDepth.Spec };
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenTimeWithoutTimezone_WhenValidatingAtFullDepth_ThenReturnsError()
    {
        // Arrange - R4 spec regex requires timezone when time is present
        // The R4 dateTime regex nests timezone inside the time group without `?`:
        //   (T hh:mm:ss(.s+)? (Z|offset) )?
        // This means timezone is mandatory whenever a time component exists
        var json = JsonNode.Parse("""{"resourceType":"Condition","recordedDate":"2023-01-01T12:00:00"}""");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new TypeCheck("recordedDate", "dateTime");
        var settings = new ValidationSettings { Depth = Ignixa.Validation.Abstractions.ValidationDepth.Full };
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldHaveSingleItem();
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenStandardDateTimeFormats_WhenValidating_ThenStillReturnsSuccess()
    {
        // Arrange - Standard dateTime formats valid at all depths
        var testCases = new[]
        {
            "2021",
            "2021-10",
            "2021-10-13",
            "2021-10-13T10:30:00Z",
            "2021-10-13T10:30:00+02:00",
            "2021-10-13T10:30:00.123Z"
        };

        foreach (var dateTime in testCases)
        {
            var json = JsonNode.Parse($$"""{"resourceType":"Condition","recordedDate":"{{dateTime}}"}""");
            var sourceNode = JsonNodeSourceNode.Create(json);
            var check = new TypeCheck("recordedDate", "dateTime");
            var settings = new ValidationSettings { Depth = Ignixa.Validation.Abstractions.ValidationDepth.Full };
            var state = new ValidationState();

            // Act
            var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

            // Assert
            result.IsValid.ShouldBeTrue($"dateTime '{dateTime}' should be valid even at Full depth");
            result.Issues.ShouldBeEmpty();
        }
    }
}
