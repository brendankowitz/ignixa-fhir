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
        Assert.True(result.IsValid, $"Expected valid dateTime for {jsonValue} but got: {(result.Issues.Count > 0 ? result.Issues[0].Message : "no issues")}");
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
        Assert.False(result.IsValid, $"Expected invalid dateTime for {jsonValue} but validation passed");
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
        Assert.True(result.IsValid, $"Expected valid date for {jsonValue} but got: {(result.Issues.Count > 0 ? result.Issues[0].Message : "no issues")}");
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
        Assert.False(result.IsValid, $"Expected invalid date for {jsonValue} but validation passed");
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
        Assert.True(result.IsValid, $"Expected valid time for {jsonValue} but got: {(result.Issues.Count > 0 ? result.Issues[0].Message : "no issues")}");
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
        Assert.False(result.IsValid, $"Expected invalid time for {jsonValue} but validation passed");
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
        Assert.True(result.IsValid, $"Expected valid instant for {jsonValue} but got: {(result.Issues.Count > 0 ? result.Issues[0].Message : "no issues")}");
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
        Assert.False(result.IsValid, $"Expected invalid instant for {jsonValue} but validation passed");
    }
}
