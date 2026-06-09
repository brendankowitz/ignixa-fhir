// <copyright file="ChoiceElementCheckTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

#pragma warning disable CA1861 // Prefer static readonly fields - not applicable for test code

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
/// Tests for ChoiceElementCheck (value[x] validation).
/// </summary>
public class ChoiceElementCheckTests
{
    #region Valid Scenarios

    [Fact]
    public void GivenSingleChoiceVariant_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Observation with single valueQuantity
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Observation"",
            ""valueQuantity"": {
                ""value"": 120,
                ""unit"": ""mmHg""
            }
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ChoiceElementCheck("value", new[] { "Quantity", "string", "boolean" });
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenNoChoiceVariant_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Observation without value[x] (optional element)
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Observation"",
            ""status"": ""final""
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ChoiceElementCheck("value", new[] { "Quantity", "string" });
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenAllowedChoiceType_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Condition with onsetDateTime
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Condition"",
            ""onsetDateTime"": ""2024-01-15T10:00:00Z""
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ChoiceElementCheck("onset", new[] { "dateTime", "Age", "Period", "Range", "string" });
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    #endregion

    #region Invalid Scenarios - Multiple Variants

    [Fact]
    public void GivenMultipleChoiceVariants_WhenValidating_ThenReturnsError()
    {
        // Arrange - Observation with BOTH valueQuantity AND valueString (invalid)
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Observation"",
            ""valueQuantity"": { ""value"": 120 },
            ""valueString"": ""text value""
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ChoiceElementCheck("value", new[] { "Quantity", "string" });
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldHaveSingleItem();
        result.Issues.ShouldContain(i => i.Code == "choice-multiple");
        result.Issues[0].Message.ShouldContain("valueQuantity");
        result.Issues[0].Message.ShouldContain("valueString");
    }

    [Fact]
    public void GivenThreeChoiceVariants_WhenValidating_ThenReturnsError()
    {
        // Arrange - Multiple choice variants
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Element"",
            ""valueString"": ""text"",
            ""valueCode"": ""code"",
            ""valueBoolean"": true
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ChoiceElementCheck("value", new[] { "string", "Code", "boolean" });
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldHaveSingleItem();
        result.Issues.ShouldContain(i => i.Code == "choice-multiple");
    }

    #endregion

    #region Invalid Scenarios - Disallowed Type

    [Fact]
    public void GivenDisallowedChoiceType_WhenValidating_ThenReturnsError()
    {
        // Arrange - Observation with valueCodeableConcept (not in allowed types)
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Observation"",
            ""valueCodeableConcept"": {
                ""coding"": [{
                    ""system"": ""http://example.org"",
                    ""code"": ""test""
                }]
            }
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ChoiceElementCheck("value", new[] { "Quantity", "string" }); // CodeableConcept NOT allowed
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldHaveSingleItem();
        result.Issues.ShouldContain(i => i.Code == "choice-invalid-type");
        result.Issues[0].Message.ShouldContain("CodeableConcept");
        result.Issues[0].Message.ShouldContain("Quantity, string"); // Shows allowed types
    }

    [Fact]
    public void GivenUnexpectedChoiceType_WhenValidating_ThenReturnsError()
    {
        // Arrange - Custom element with unexpected type
        var json = JsonNode.Parse(@"{
            ""valueAttachment"": {
                ""contentType"": ""application/pdf""
            }
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ChoiceElementCheck("value", new[] { "string", "integer", "boolean" });
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldHaveSingleItem();
        result.Issues.ShouldContain(i => i.Code == "choice-invalid-type");
        result.Issues[0].Message.ShouldContain("Attachment");
    }

    #endregion

    #region Type Name Normalization

    [Fact]
    public void GivenLowercaseTypeName_WhenValidating_ThenNormalizes()
    {
        // Arrange - Type name "string" (lowercase) should match "valueString"
        var json = JsonNode.Parse(@"{
            ""valueString"": ""test""
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ChoiceElementCheck("value", new[] { "string" }); // lowercase
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenUppercaseTypeName_WhenValidating_ThenNormalizes()
    {
        // Arrange - Type name "String" (uppercase) should match "valueString"
        var json = JsonNode.Parse(@"{
            ""valueString"": ""test""
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ChoiceElementCheck("value", new[] { "String" }); // uppercase
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    #endregion

    #region Primitive Value Rules

    private static ValidationResult ValidateChoice(string json, string[] allowedTypes)
    {
        var sourceNode = JsonNodeSourceNode.Create(JsonNode.Parse(json));
        var check = new ChoiceElementCheck("value", allowedTypes);
        return check.Validate(
            sourceNode.ToElement(TestSchemaProvider.GetR4Schema()),
            new ValidationSettings(),
            new ValidationState());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("\"true\"")]
    public void GivenChoiceBooleanWithNonBooleanValue_WhenValidating_ThenReturnsError(string value)
    {
        var result = ValidateChoice(
            $@"{{ ""valueBoolean"": {value} }}",
            new[] { "boolean", "string", "integer" });

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "type-1");
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void GivenChoiceBooleanWithBooleanValue_WhenValidating_ThenReturnsSuccess(string value)
    {
        var result = ValidateChoice(
            $@"{{ ""valueBoolean"": {value} }}",
            new[] { "boolean", "string" });

        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("3.1")]            // fractional
    [InlineData("2147483648")]    // > int32 max
    [InlineData("true")]          // wrong JSON kind
    public void GivenChoiceIntegerWithInvalidValue_WhenValidating_ThenReturnsError(string value)
    {
        var result = ValidateChoice(
            $@"{{ ""valueInteger"": {value} }}",
            new[] { "integer", "boolean" });

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "type-1");
    }

    [Fact]
    public void GivenChoiceIntegerWithValidValue_WhenValidating_ThenReturnsSuccess()
    {
        var result = ValidateChoice(@"{ ""valueInteger"": 42 }", new[] { "integer" });
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void GivenChoiceUnsignedIntWithNegativeValue_WhenValidating_ThenReturnsError()
    {
        var result = ValidateChoice(@"{ ""valueUnsignedInt"": -1 }", new[] { "unsignedInt" });
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "type-1");
    }

    [Theory]
    [InlineData("\"2000-13\"")]       // invalid month
    [InlineData("\"2000-00\"")]       // invalid month
    [InlineData("\"201\"")]           // too few year digits
    [InlineData("\"\"")]              // empty
    public void GivenChoiceDateTimeWithMalformedValue_WhenValidating_ThenReturnsError(string value)
    {
        var result = ValidateChoice(
            $@"{{ ""valueDateTime"": {value} }}",
            new[] { "dateTime", "string" });

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "type-1");
    }

    [Theory]
    [InlineData("\"2024\"")]
    [InlineData("\"2024-01\"")]
    [InlineData("\"2024-01-15\"")]
    [InlineData("\"2024-01-15T10:00:00Z\"")]
    public void GivenChoiceDateTimeWithValidValue_WhenValidating_ThenReturnsSuccess(string value)
    {
        var result = ValidateChoice(
            $@"{{ ""valueDateTime"": {value} }}",
            new[] { "dateTime", "string" });

        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("valueString")]
    [InlineData("valueCode")]
    [InlineData("valueUri")]
    [InlineData("valueId")]
    public void GivenChoiceStringPrimitiveWithEmptyValue_WhenValidating_ThenReturnsError(string variant)
    {
        var result = ValidateChoice(
            $@"{{ ""{variant}"": """" }}",
            new[] { "string", "code", "uri", "id" });

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "type-1");
    }

    [Fact]
    public void GivenInvalidChoicePrimitive_WhenValidating_ThenIssueExpressionUsesOfType()
    {
        var sourceNode = JsonNodeSourceNode.Create(JsonNode.Parse(
            @"{ ""resourceType"": ""Observation"", ""valueBoolean"": 0 }"));
        var check = new ChoiceElementCheck("value", new[] { "boolean", "string" });
        var result = check.Validate(
            sourceNode.ToElement(TestSchemaProvider.GetR4Schema()),
            new ValidationSettings(),
            new ValidationState());

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Path.EndsWith("value.ofType(boolean)", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenChoiceComplexType_WhenValidating_ThenSkipsPrimitiveRules()
    {
        // A complex variant (Quantity) is not subject to primitive value rules here.
        var result = ValidateChoice(
            @"{ ""valueQuantity"": { ""value"": 120, ""unit"": ""mmHg"" } }",
            new[] { "Quantity", "boolean" });

        result.IsValid.ShouldBeTrue();
    }

    #endregion

    #region Real-World FHIR Scenarios

    [Fact]
    public void GivenObservationWithValidValue_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Real Observation with valueQuantity
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Observation"",
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""8867-4""
                }]
            },
            ""valueQuantity"": {
                ""value"": 98.6,
                ""unit"": ""degF"",
                ""system"": ""http://unitsofmeasure.org"",
                ""code"": ""[degF]""
            }
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ChoiceElementCheck("value", new[] { "Quantity", "CodeableConcept", "string", "boolean", "integer", "Range", "Ratio", "SampledData", "time", "dateTime", "Period" });
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenConditionWithValidOnset_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Real Condition with onsetAge
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Condition"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://snomed.info/sct"",
                    ""code"": ""38341003""
                }]
            },
            ""onsetAge"": {
                ""value"": 45,
                ""unit"": ""years"",
                ""system"": ""http://unitsofmeasure.org"",
                ""code"": ""a""
            }
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ChoiceElementCheck("onset", new[] { "dateTime", "Age", "Period", "Range", "string" });
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    #endregion
}
