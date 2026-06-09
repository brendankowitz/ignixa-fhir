// <copyright file="FhirPrimitiveValidatorConformanceTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

#pragma warning disable CA1861 // Prefer static readonly fields - not applicable for test code

using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// fhir262 conformance tests for FHIR date/dateTime/time/instant primitive validation.
/// Covers fractional-second digit cap (max 9) and calendar-validity (leap-year aware).
/// </summary>
public class FhirPrimitiveValidatorConformanceTests
{
    private static ValidationResult ValidatePrimitive(string fhirPropertyName, string jsonValue, string[] allowedTypes)
    {
        var json = JsonNode.Parse($@"{{ ""{fhirPropertyName}"": {jsonValue} }}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ChoiceElementCheck("value", allowedTypes);
        return check.Validate(
            sourceNode.ToElement(TestSchemaProvider.GetR4Schema()),
            new ValidationSettings(),
            new ValidationState());
    }

    private static ValidationResult ValidateNode(JsonObject json, string[] allowedTypes)
    {
        var sourceNode = JsonNodeSourceNode.Create(json);
        return new ChoiceElementCheck("value", allowedTypes).Validate(
            sourceNode.ToElement(TestSchemaProvider.GetR4Schema()),
            new ValidationSettings(),
            new ValidationState());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(2147483647)]
    public void GivenProgrammaticIntegerValue_WhenValidating_ThenAcceptsWholeValue(int value)
    {
        // Built programmatically (CLR int-backed JsonValue), not parsed from text. Guards
        // against TryGetValue<long> failing on non-JsonElement-backed nodes (e.g. faker- or
        // code-constructed resources), which previously misreported a valid integer as fractional.
        var result = ValidateNode(new JsonObject { ["valueInteger"] = value }, new[] { "integer" });
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void GivenProgrammaticPositiveIntValue_WhenValidating_ThenAcceptsWholeValue()
    {
        var result = ValidateNode(new JsonObject { ["valuePositiveInt"] = 2 }, new[] { "positiveInt" });
        result.IsValid.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // valueDateTime — ACCEPTS
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("\"0001\"")]
    [InlineData("\"2018\"")]
    [InlineData("\"1973-06\"")]
    [InlineData("\"1905-08-23\"")]
    [InlineData("\"2000-02-29\"")]
    [InlineData("\"2024-02-29\"")]
    [InlineData("\"2015-02-07T13:28:17-05:00\"")]
    [InlineData("\"2017-01-01T00:00:00Z\"")]
    [InlineData("\"2017-01-01T00:00:00.000Z\"")]
    [InlineData("\"2017-01-01T00:00:00.000000000Z\"")]
    [InlineData("\"2015-02-07T13:28:17+14:00\"")]
    public void GivenValidDateTime_WhenValidating_ThenReturnsSuccess(string jsonValue)
    {
        var result = ValidatePrimitive("valueDateTime", jsonValue, ["dateTime"]);
        result.IsValid.ShouldBeTrue($"Expected valid dateTime for {jsonValue} but got: {(result.Issues.Count > 0 ? result.Issues[0].Message : "no issues")}");
    }

    // -------------------------------------------------------------------------
    // valueDateTime — REJECTS
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("\"2000-13\"")]
    [InlineData("\"2000-00\"")]
    [InlineData("\"2000-01-00\"")]
    [InlineData("\"2024-02-31\"")]
    [InlineData("\"2024-01-35\"")]
    [InlineData("\"2023-02-29\"")]
    [InlineData("\"1900-02-29\"")]
    [InlineData("\"2017-01-01T23:59:61Z\"")]
    [InlineData("\"2017-01-01T00:60:00Z\"")]
    [InlineData("\"2017-01-01T24:00:00Z\"")]
    [InlineData("\"2017-01-01T00:00:00.0000000000Z\"")]
    [InlineData("\"2017-01-01t00:00:00z\"")]
    [InlineData("\"2015-02-07T13:28:17+24:00\"")]
    [InlineData("\"2015-02-07T13:28:17+14:01\"")]
    public void GivenInvalidDateTime_WhenValidating_ThenReturnsError(string jsonValue)
    {
        var result = ValidatePrimitive("valueDateTime", jsonValue, ["dateTime"]);
        result.IsValid.ShouldBeFalse($"Expected invalid dateTime for {jsonValue} but validation passed");
    }

    // -------------------------------------------------------------------------
    // valueDate — ACCEPTS
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("\"0001\"")]
    [InlineData("\"2018\"")]
    [InlineData("\"1973-06\"")]
    [InlineData("\"1905-08-23\"")]
    [InlineData("\"2000-01-02\"")]
    [InlineData("\"2024-02-29\"")]
    [InlineData("\"2000-02-29\"")]
    public void GivenValidDate_WhenValidating_ThenReturnsSuccess(string jsonValue)
    {
        var result = ValidatePrimitive("valueDate", jsonValue, ["date"]);
        result.IsValid.ShouldBeTrue($"Expected valid date for {jsonValue} but got: {(result.Issues.Count > 0 ? result.Issues[0].Message : "no issues")}");
    }

    // -------------------------------------------------------------------------
    // valueDate — REJECTS
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("\"0000\"")]
    [InlineData("\"201\"")]
    [InlineData("\"2000-13\"")]
    [InlineData("\"2000-01-32\"")]
    [InlineData("\"2024-02-31\"")]
    [InlineData("\"2023-02-29\"")]
    [InlineData("\"1900-02-29\"")]
    [InlineData("\"2017-01-01T00:00:00Z\"")]
    public void GivenInvalidDate_WhenValidating_ThenReturnsError(string jsonValue)
    {
        var result = ValidatePrimitive("valueDate", jsonValue, ["date"]);
        result.IsValid.ShouldBeFalse($"Expected invalid date for {jsonValue} but validation passed");
    }

    // -------------------------------------------------------------------------
    // valueTime — ACCEPTS
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("\"00:00:00\"")]
    [InlineData("\"12:03:00\"")]
    [InlineData("\"23:59:59\"")]
    [InlineData("\"13:37:12.132\"")]
    [InlineData("\"12:00:60\"")]
    public void GivenValidTime_WhenValidating_ThenReturnsSuccess(string jsonValue)
    {
        var result = ValidatePrimitive("valueTime", jsonValue, ["time"]);
        result.IsValid.ShouldBeTrue($"Expected valid time for {jsonValue} but got: {(result.Issues.Count > 0 ? result.Issues[0].Message : "no issues")}");
    }

    // -------------------------------------------------------------------------
    // valueTime — REJECTS
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("\"23:02\"")]
    [InlineData("\"24:00:00\"")]
    [InlineData("\"12:60:00\"")]
    [InlineData("\"12:00:61\"")]
    [InlineData("\"12:00:00Z\"")]
    [InlineData("\"12:00:00.0000000000\"")]
    [InlineData("\"2015-02-07T13:28:17\"")]
    public void GivenInvalidTime_WhenValidating_ThenReturnsError(string jsonValue)
    {
        var result = ValidatePrimitive("valueTime", jsonValue, ["time"]);
        result.IsValid.ShouldBeFalse($"Expected invalid time for {jsonValue} but validation passed");
    }

    // -------------------------------------------------------------------------
    // valueInstant — ACCEPTS
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("\"2015-02-07T13:28:17.239+02:00\"")]
    [InlineData("\"2017-01-01T00:00:00Z\"")]
    [InlineData("\"2017-01-01T00:00:00.000000000Z\"")]
    [InlineData("\"2017-01-01T00:00:60Z\"")]
    public void GivenValidInstant_WhenValidating_ThenReturnsSuccess(string jsonValue)
    {
        var result = ValidatePrimitive("valueInstant", jsonValue, ["instant"]);
        result.IsValid.ShouldBeTrue($"Expected valid instant for {jsonValue} but got: {(result.Issues.Count > 0 ? result.Issues[0].Message : "no issues")}");
    }

    // -------------------------------------------------------------------------
    // valueInstant — REJECTS
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("\"2017-01-01t00:00:00z\"")]
    [InlineData("\"2017-01-01T00:00:00\"")]
    [InlineData("\"2015-02-07T13:28:17.239\"")]
    [InlineData("\"2018\"")]
    [InlineData("\"2017-01-01T00:00:00.0000000000Z\"")]
    public void GivenInvalidInstant_WhenValidating_ThenReturnsError(string jsonValue)
    {
        var result = ValidatePrimitive("valueInstant", jsonValue, ["instant"]);
        result.IsValid.ShouldBeFalse($"Expected invalid instant for {jsonValue} but validation passed");
    }

    // -------------------------------------------------------------------------
    // Integer-family boundary accepts/rejects (32-bit edges, unsignedInt/positiveInt floor).
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("valueUnsignedInt", "0", "unsignedInt")]
    [InlineData("valuePositiveInt", "1", "positiveInt")]
    [InlineData("valueInteger", "2147483647", "integer")]
    [InlineData("valueInteger", "-2147483648", "integer")]
    public void GivenIntegerFamilyBoundaryValue_WhenValidating_ThenReturnsSuccess(string property, string jsonValue, string type)
    {
        var result = ValidatePrimitive(property, jsonValue, [type]);
        result.IsValid.ShouldBeTrue($"Expected valid {type} for {jsonValue} but got: {(result.Issues.Count > 0 ? result.Issues[0].Message : "no issues")}");
    }

    [Fact]
    public void GivenIntegerBelowInt32Min_WhenValidating_ThenReturnsError()
    {
        var result = ValidatePrimitive("valueInteger", "-2147483649", ["integer"]);
        result.IsValid.ShouldBeFalse("Expected invalid integer below Int32.MinValue but validation passed");
    }

    // -------------------------------------------------------------------------
    // integer64 and decimal (kind/whole-number rules).
    // -------------------------------------------------------------------------

    [Fact]
    public void GivenInteger64AtInt64Max_WhenValidating_ThenReturnsSuccess()
    {
        var result = ValidatePrimitive("valueInteger64", "9223372036854775807", ["integer64"]);
        result.IsValid.ShouldBeTrue($"Expected valid integer64 but got: {(result.Issues.Count > 0 ? result.Issues[0].Message : "no issues")}");
    }

    [Fact]
    public void GivenInteger64WithFraction_WhenValidating_ThenReturnsError()
    {
        var result = ValidatePrimitive("valueInteger64", "3.1", ["integer64"]);
        result.IsValid.ShouldBeFalse("Expected invalid integer64 (fractional) but validation passed");
    }

    [Fact]
    public void GivenDecimalWithFraction_WhenValidating_ThenReturnsSuccess()
    {
        var result = ValidatePrimitive("valueDecimal", "3.14", ["decimal"]);
        result.IsValid.ShouldBeTrue($"Expected valid decimal but got: {(result.Issues.Count > 0 ? result.Issues[0].Message : "no issues")}");
    }

    [Theory]
    [InlineData("\"3.14\"")] // string, not a JSON number
    [InlineData("true")]     // boolean, not a JSON number
    public void GivenDecimalWithNonNumberValue_WhenValidating_ThenReturnsError(string jsonValue)
    {
        var result = ValidatePrimitive("valueDecimal", jsonValue, ["decimal"]);
        result.IsValid.ShouldBeFalse($"Expected invalid decimal for {jsonValue} but validation passed");
    }

    // -------------------------------------------------------------------------
    // time fractional-second upper bound (9 digits) — locks {1,9} for time.
    // -------------------------------------------------------------------------

    [Fact]
    public void GivenTimeWithNineFractionalDigits_WhenValidating_ThenReturnsSuccess()
    {
        var result = ValidatePrimitive("valueTime", "\"12:00:00.000000000\"", ["time"]);
        result.IsValid.ShouldBeTrue($"Expected valid time but got: {(result.Issues.Count > 0 ? result.Issues[0].Message : "no issues")}");
    }

    // -------------------------------------------------------------------------
    // Choice primitive carrying BOTH a value AND a "_value" shadow (extensions/id).
    // Regression guard: Meta<JsonNode>() returns the shadow object for these; the
    // validator must inspect the VALUE node, not reject the shadow as type-1.
    // -------------------------------------------------------------------------

    private static ValidationResult ValidatePrimitiveWithShadow(string property, string valueJson, string shadowJson, string[] allowedTypes)
    {
        var json = JsonNode.Parse($@"{{ ""resourceType"": ""Observation"", ""{property}"": {valueJson}, ""_{property}"": {shadowJson} }}");
        var sourceNode = JsonNodeSourceNode.Create(json!);
        var check = new ChoiceElementCheck("value", allowedTypes);
        return check.Validate(
            sourceNode.ToElement(TestSchemaProvider.GetR4Schema()),
            new ValidationSettings(),
            new ValidationState());
    }

    [Fact]
    public void GivenDateTimeValueWithExtensionShadow_WhenValidating_ThenReturnsSuccess()
    {
        var result = ValidatePrimitiveWithShadow(
            "valueDateTime",
            "\"2024-01-15\"",
            @"{ ""extension"": [ { ""url"": ""http://x"", ""valueCode"": ""estimated"" } ] }",
            ["dateTime", "string", "boolean"]);

        result.IsValid.ShouldBeTrue($"Expected valid dateTime+shadow but got: {(result.Issues.Count > 0 ? result.Issues[0].Message : "no issues")}");
    }

    [Fact]
    public void GivenBooleanValueWithExtensionShadow_WhenValidating_ThenReturnsSuccess()
    {
        var result = ValidatePrimitiveWithShadow(
            "valueBoolean",
            "true",
            @"{ ""extension"": [ { ""url"": ""http://x"", ""valueCode"": ""estimated"" } ] }",
            ["boolean", "string"]);

        result.IsValid.ShouldBeTrue($"Expected valid boolean+shadow but got: {(result.Issues.Count > 0 ? result.Issues[0].Message : "no issues")}");
    }

    [Fact]
    public void GivenStringValueWithIdShadow_WhenValidating_ThenReturnsSuccess()
    {
        var result = ValidatePrimitiveWithShadow(
            "valueString",
            "\"x\"",
            @"{ ""id"": ""1"" }",
            ["string", "boolean"]);

        result.IsValid.ShouldBeTrue($"Expected valid string+shadow but got: {(result.Issues.Count > 0 ? result.Issues[0].Message : "no issues")}");
    }

    [Fact]
    public void GivenMalformedBooleanValueWithShadow_WhenValidating_ThenReturnsError()
    {
        // The shadow must not mask a malformed value: 0 is still not a JSON boolean.
        var result = ValidatePrimitiveWithShadow(
            "valueBoolean",
            "0",
            @"{ ""extension"": [ { ""url"": ""http://x"", ""valueCode"": ""estimated"" } ] }",
            ["boolean", "string", "integer"]);

        result.IsValid.ShouldBeFalse("Expected invalid boolean (0) even with shadow but validation passed");
        result.Issues.ShouldContain(i => i.Code == "type-1");
    }

    [Fact]
    public void GivenMalformedDateTimeValueWithShadow_WhenValidating_ThenReturnsError()
    {
        var result = ValidatePrimitiveWithShadow(
            "valueDateTime",
            "\"2000-13\"",
            @"{ ""extension"": [ { ""url"": ""http://x"", ""valueCode"": ""estimated"" } ] }",
            ["dateTime", "string"]);

        result.IsValid.ShouldBeFalse("Expected invalid dateTime (2000-13) even with shadow but validation passed");
        result.Issues.ShouldContain(i => i.Code == "type-1");
    }
}
