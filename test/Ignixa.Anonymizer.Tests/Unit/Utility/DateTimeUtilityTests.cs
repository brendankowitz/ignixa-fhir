// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using Ignixa.Anonymizer.Processors;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Anonymizer.Utility;
using Xunit;

namespace Ignixa.Anonymizer.Core.UnitTests.Utility;

public class DateTimeUtilityTests
{
    private readonly R4CoreSchemaProvider _schema = new();

    public static IEnumerable<object[]> GetDateDataForPartialRedact()
    {
        yield return new object[] { "2015", "2015" };
        yield return new object[] { "2015-02", "2015" };
        yield return new object[] { "2015-02-07", "2015" };
        yield return new object[] { "1925-02-07", null };
    }

    public static IEnumerable<object[]> GetDateDataForRedact()
    {
        yield return new object[] { "2015" };
        yield return new object[] { "2015-02" };
        yield return new object[] { "2015-02-07" };
        yield return new object[] { "1925-02-07" };
    }

    public static IEnumerable<object[]> GetDateDataForDateShift()
    {
        yield return new object[] { "2015-02-07", "2014-12-19", "2015-03-29" };
        yield return new object[] { "2020-01-17", "2019-11-28", "2020-03-07" };
        yield return new object[] { "1998-10-02", "1998-08-13", "1998-11-21" };
        yield return new object[] { "1975-12-26", "1975-11-06", "1976-02-14" };
    }

    public static IEnumerable<object[]> GetDateDataForDateShiftButShouldBeRedacted()
    {
        yield return new object[] { "2015-02", "2015" };
        yield return new object[] { "1925-02-07", null };
    }

    public static IEnumerable<object[]> GetDateTimeDataForRedact()
    {
        yield return new object[] { "2015", "2015" };
        yield return new object[] { "2015-02", "2015" };
        yield return new object[] { "2015-02-07", "2015" };
        yield return new object[] { "2015-02-07T13:28:17-05:00", "2015" };
        yield return new object[] { "1925-02-07T13:28:17-05:00", null };
    }

    public static IEnumerable<object[]> GetInstantDataForRedact()
    {
        yield return new object[] { "2015-02-07T13:28:17-05:00", "2015" };
        yield return new object[] { "1925-02-07T13:28:17-05:00", null };
    }

    public static IEnumerable<object[]> GetDateTimeDataForDateShiftFormatTest()
    {
        yield return new object[] { "dummy", "2015-02-07", "2015-01-17" };
        yield return new object[] { "dummy", "2015-02-07T13:28:17-05:00", "2015-01-17T00:00:00-05:00" };
        yield return new object[] { "dummy", "2015-02-07T13:28:17+05:00", "2015-01-17T00:00:00+05:00" };
        yield return new object[] { "dummy", "2015-02-07T13:28:17Z", "2015-01-17T00:00:00Z" };
        yield return new object[] { "dummy", "2015-02-07T13:28:17.12345-05:00", "2015-01-17T00:00:00.00000-05:00" };
    }

    public static IEnumerable<object[]> GetAgeDataForPartialRedact()
    {
        yield return new object[] { 92 };
        yield return new object[] { 57 };
    }

    public static IEnumerable<object[]> GetAgeDataForRedact()
    {
        yield return new object[] { 101 };
        yield return new object[] { 35 };
    }

    [Theory]
    [MemberData(nameof(GetDateDataForPartialRedact))]
    public void GivenADate_WhenPartialRedact_ThenDateShouldBeRedacted(string dateValue, string expectedValue)
    {
        var json = $$$"""{"resourceType":"Patient","birthDate":"{{{dateValue}}}"}""";
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);
        var node = element.Children("birthDate").First();
        var result = DateTimeUtility.RedactDateNode(node, true);

        resourceNode.InvalidateCaches();
        var updated = resourceNode.ToElement(_schema);
        var updatedNode = updated.Children("birthDate").FirstOrDefault();
        Assert.Equal(expectedValue, updatedNode?.Value?.ToString());
        Assert.True(result.WasModified);
        Assert.Equal(AnonymizationOperations.Redact, result.OperationType);
    }

    [Theory]
    [MemberData(nameof(GetDateDataForRedact))]
    public void GivenADate_WhenRedact_ThenDateShouldBeRedacted(string dateValue)
    {
        var json = $$$"""{"resourceType":"Patient","birthDate":"{{{dateValue}}}"}""";
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);
        var node = element.Children("birthDate").First();
        var result = DateTimeUtility.RedactDateNode(node, false);

        resourceNode.InvalidateCaches();
        var updated = resourceNode.ToElement(_schema);
        var updatedNode = updated.Children("birthDate").FirstOrDefault();
        Assert.Null(updatedNode?.Value);
        Assert.True(result.WasModified);
        Assert.Equal(AnonymizationOperations.Redact, result.OperationType);
    }

    [Theory]
    [MemberData(nameof(GetDateDataForDateShift))]
    public void GivenADate_WhenDateShift_ThenDateShouldBeShifted(string dateValue, string minExpected, string maxExpected)
    {
        var json = $$$"""{"resourceType":"Patient","birthDate":"{{{dateValue}}}"}""";
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);
        var node = element.Children("birthDate").First();
        var result = DateTimeUtility.ShiftDateNode(node, string.Empty, string.Empty, null, true);

        resourceNode.InvalidateCaches();
        var updated = resourceNode.ToElement(_schema);
        var updatedNode = updated.Children("birthDate").First();
        Assert.True(DateTime.Parse(minExpected) <= DateTime.Parse(updatedNode.Value.ToString()));
        Assert.True(DateTime.Parse(maxExpected) >= DateTime.Parse(updatedNode.Value.ToString()));
        Assert.True(result.WasModified);
        Assert.Equal(AnonymizationOperations.Perturb, result.OperationType);
    }

    [Theory]
    [MemberData(nameof(GetDateDataForDateShiftButShouldBeRedacted))]
    public void GivenADateWithoutDayOrAgeOver89_WhenDateShift_ThenDateShouldBeRedacted(string dateValue, string expectedValue)
    {
        var json = $$$"""{"resourceType":"Patient","birthDate":"{{{dateValue}}}"}""";
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);
        var node = element.Children("birthDate").First();
        var result = DateTimeUtility.ShiftDateNode(node, string.Empty, string.Empty, null, true);

        resourceNode.InvalidateCaches();
        var updated = resourceNode.ToElement(_schema);
        var updatedNode = updated.Children("birthDate").FirstOrDefault();
        Assert.Equal(expectedValue, updatedNode?.Value?.ToString());
        Assert.True(result.WasModified);
        Assert.Equal(AnonymizationOperations.Redact, result.OperationType);
    }

    [Theory]
    [MemberData(nameof(GetDateTimeDataForRedact))]
    public void GivenADateTime_WhenRedact_ThenDateTimeShouldBeRedacted(string dateTimeValue, string expectedValue)
    {
        string json;
        string fieldName;
        bool isDateTime = dateTimeValue.Contains('T');
        if (isDateTime)
        {
            json = $$$"""{"resourceType":"Observation","effectiveDateTime":"{{{dateTimeValue}}}"}""";
            fieldName = "effectiveDateTime";
        }
        else
        {
            json = $$$"""{"resourceType":"Patient","birthDate":"{{{dateTimeValue}}}"}""";
            fieldName = "birthDate";
        }
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);
        var node = element.Children(fieldName).First();

        var result = isDateTime
            ? DateTimeUtility.RedactDateTimeAndInstantNode(node, true)
            : DateTimeUtility.RedactDateNode(node, true);

        resourceNode.InvalidateCaches();
        var updated = resourceNode.ToElement(_schema);
        var updatedNode = updated.Children(fieldName).FirstOrDefault();
        Assert.Equal(expectedValue, updatedNode?.Value?.ToString());
        Assert.True(result.WasModified);
        Assert.Equal(AnonymizationOperations.Redact, result.OperationType);
    }

    [Theory]
    [MemberData(nameof(GetInstantDataForRedact))]
    public void GivenAnInstant_WhenRedact_ThenInstantShouldBeRedacted(string instantValue, string expectedValue)
    {
        var json = $$$"""{"resourceType":"Observation","issued":"{{{instantValue}}}"}""";
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);
        var node = element.Children("issued").First();
        var result = DateTimeUtility.RedactDateTimeAndInstantNode(node, true);

        resourceNode.InvalidateCaches();
        var updated = resourceNode.ToElement(_schema);
        var updatedNode = updated.Children("issued").FirstOrDefault();
        Assert.Equal(expectedValue, updatedNode?.Value?.ToString());
        Assert.True(result.WasModified);
        Assert.Equal(AnonymizationOperations.Redact, result.OperationType);
    }

    [Theory]
    [MemberData(nameof(GetDateTimeDataForDateShiftFormatTest))]
    public void GivenADateTime_WhenDateShift_ThenDateTimeFormatShouldNotChange(string dateShiftKey, string dateTimeValue, string expectedValue)
    {
        string json;
        string fieldName;
        bool isDateTime = dateTimeValue.Contains('T');
        if (isDateTime)
        {
            json = $$$"""{"resourceType":"Observation","effectiveDateTime":"{{{dateTimeValue}}}"}""";
            fieldName = "effectiveDateTime";
        }
        else
        {
            json = $$$"""{"resourceType":"Patient","birthDate":"{{{dateTimeValue}}}"}""";
            fieldName = "birthDate";
        }
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);
        var node = element.Children(fieldName).First();

        var result = isDateTime
            ? DateTimeUtility.ShiftDateTimeAndInstantNode(node, dateShiftKey, string.Empty, null, true)
            : DateTimeUtility.ShiftDateNode(node, dateShiftKey, string.Empty, null, true);

        resourceNode.InvalidateCaches();
        var updated = resourceNode.ToElement(_schema);
        var updatedNode = updated.Children(fieldName).First();
        Assert.Equal(expectedValue, updatedNode.Value.ToString());
        Assert.True(result.WasModified);
        Assert.Equal(AnonymizationOperations.Perturb, result.OperationType);
    }

    [Theory]
    [MemberData(nameof(GetAgeDataForPartialRedact))]
    public void GivenAnAge_WhenPartialRedact_ThenAgeOver89ShouldBeRedacted(int ageValue)
    {
        var json = $$$"""{"resourceType":"Condition","onsetAge":{"value":{{{ageValue}}},"unit":"a","system":"http://unitsofmeasure.org","code":"a"}}""";
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);
        var node = element.Children("onsetAge").First().Children("value").First();
        var result = DateTimeUtility.RedactAgeDecimalNode(node, true);

        resourceNode.InvalidateCaches();
        var updated = resourceNode.ToElement(_schema);
        var updatedNode = updated.Children("onsetAge").First().Children("value").FirstOrDefault();
        Assert.Equal(ageValue > 89 ? null : ageValue.ToString(), updatedNode?.Value?.ToString());
        Assert.True(result.WasModified);
        Assert.Equal(AnonymizationOperations.Redact, result.OperationType);
    }

    [Theory]
    [MemberData(nameof(GetAgeDataForRedact))]
    public void GivenAnAge_WhenRedact_ThenAgeShouldBeRedacted(int ageValue)
    {
        var json = $$$"""{"resourceType":"Condition","onsetAge":{"value":{{{ageValue}}},"unit":"a","system":"http://unitsofmeasure.org","code":"a"}}""";
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);
        var node = element.Children("onsetAge").First().Children("value").First();
        var result = DateTimeUtility.RedactAgeDecimalNode(node, false);

        resourceNode.InvalidateCaches();
        var updated = resourceNode.ToElement(_schema);
        var updatedNode = updated.Children("onsetAge").First().Children("value").FirstOrDefault();
        Assert.Null(updatedNode?.Value);
        Assert.True(result.WasModified);
        Assert.Equal(AnonymizationOperations.Redact, result.OperationType);
    }
}
