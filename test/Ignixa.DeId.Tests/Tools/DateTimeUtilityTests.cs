// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.DeId.Processors;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.DeId.Tools;

namespace Ignixa.DeId.Tests.Tools;

public class DateTimeToolTests
{
    private readonly R4CoreSchemaProvider _schema = new();

    public static IEnumerable<object[]> GetDateDataForPartialRedact()
    {
        yield return ["2015", "2015"];
        yield return ["2015-02", "2015"];
        yield return ["2015-02-07", "2015"];
        yield return ["1925-02-07", null!];
    }

    public static IEnumerable<object[]> GetDateDataForRedact()
    {
        yield return ["2015"];
        yield return ["2015-02"];
        yield return ["2015-02-07"];
        yield return ["1925-02-07"];
    }

    public static IEnumerable<object[]> GetDateDataForDateShift()
    {
        yield return ["2015-02-07", "2014-12-19", "2015-03-29"];
        yield return ["2020-01-17", "2019-11-28", "2020-03-07"];
        yield return ["1998-10-02", "1998-08-13", "1998-11-21"];
        yield return ["1975-12-26", "1975-11-06", "1976-02-14"];
    }

    public static IEnumerable<object[]> GetDateDataForDateShiftButShouldBeRedacted()
    {
        yield return ["2015-02", "2015"];
        yield return ["1925-02-07", null!];
    }

    public static IEnumerable<object[]> GetDateTimeDataForRedact()
    {
        yield return ["2015", "2015"];
        yield return ["2015-02", "2015"];
        yield return ["2015-02-07", "2015"];
        yield return ["2015-02-07T13:28:17-05:00", "2015"];
        yield return ["1925-02-07T13:28:17-05:00", null!];
    }

    public static IEnumerable<object[]> GetInstantDataForRedact()
    {
        yield return ["2015-02-07T13:28:17-05:00", "2015"];
        yield return ["1925-02-07T13:28:17-05:00", null!];
    }

    public static IEnumerable<object[]> GetDateTimeDataForDateShiftFormatTest()
    {
        yield return ["dummy", "2015-02-07", "2015-01-17"];
        yield return ["dummy", "2015-02-07T13:28:17-05:00", "2015-01-17T00:00:00-05:00"];
        yield return ["dummy", "2015-02-07T13:28:17+05:00", "2015-01-17T00:00:00+05:00"];
        yield return ["dummy", "2015-02-07T13:28:17Z", "2015-01-17T00:00:00Z"];
        yield return ["dummy", "2015-02-07T13:28:17.12345-05:00", "2015-01-17T00:00:00.00000-05:00"];
    }

    public static IEnumerable<object[]> GetAgeDataForPartialRedact()
    {
        yield return [92];
        yield return [57];
    }

    public static IEnumerable<object[]> GetAgeDataForRedact()
    {
        yield return [101];
        yield return [35];
    }

    [Theory]
    [MemberData(nameof(GetDateDataForPartialRedact))]
    public void GivenADate_WhenPartialRedact_ThenDateShouldBeRedacted(string dateValue, string expectedValue)
    {
        // Arrange
        var json = $$$"""{"resourceType":"Patient","birthDate":"{{{dateValue}}}"}""";
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);
        var node = element.Children("birthDate").First();

        // Act
        var result = DateTimeTool.RedactDateNode(node, true);

        // Assert
        resourceNode.InvalidateCaches();
        var updated = resourceNode.ToElement(_schema);
        var updatedNode = updated.Children("birthDate").FirstOrDefault();
        updatedNode?.Value?.ToString().ShouldBe(expectedValue);
        result.WasModified.ShouldBeTrue();
        result.OperationType.ShouldBe(DeIdOperations.Redact);
    }

    [Theory]
    [MemberData(nameof(GetDateDataForRedact))]
    public void GivenADate_WhenRedact_ThenDateValueShouldBeNull(string dateValue)
    {
        // Arrange
        var json = $$$"""{"resourceType":"Patient","birthDate":"{{{dateValue}}}"}""";
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);
        var node = element.Children("birthDate").First();

        // Act
        var result = DateTimeTool.RedactDateNode(node, false);

        // Assert
        resourceNode.InvalidateCaches();
        var updated = resourceNode.ToElement(_schema);
        var updatedNode = updated.Children("birthDate").FirstOrDefault();
        updatedNode?.Value.ShouldBeNull();
        result.WasModified.ShouldBeTrue();
        result.OperationType.ShouldBe(DeIdOperations.Redact);
    }

    [Theory]
    [MemberData(nameof(GetDateDataForDateShift))]
    public void GivenADate_WhenDateShift_ThenDateShouldBeWithinExpectedRange(string dateValue, string minExpected, string maxExpected)
    {
        // Arrange
        var json = $$$"""{"resourceType":"Patient","birthDate":"{{{dateValue}}}"}""";
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);
        var node = element.Children("birthDate").First();

        // Act
        var result = DateTimeTool.ShiftDateNode(node, string.Empty, string.Empty, null, true);

        // Assert
        resourceNode.InvalidateCaches();
        var updated = resourceNode.ToElement(_schema);
        var updatedNode = updated.Children("birthDate").First();
        DateTime.Parse(updatedNode.Value.ToString()).ShouldBeGreaterThanOrEqualTo(DateTime.Parse(minExpected));
        DateTime.Parse(updatedNode.Value.ToString()).ShouldBeLessThanOrEqualTo(DateTime.Parse(maxExpected));
        result.WasModified.ShouldBeTrue();
        result.OperationType.ShouldBe(DeIdOperations.Perturb);
    }

    [Theory]
    [MemberData(nameof(GetDateDataForDateShiftButShouldBeRedacted))]
    public void GivenADateWithoutDayOrAgeOver89_WhenDateShift_ThenDateShouldBeRedacted(string dateValue, string expectedValue)
    {
        // Arrange
        var json = $$$"""{"resourceType":"Patient","birthDate":"{{{dateValue}}}"}""";
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);
        var node = element.Children("birthDate").First();

        // Act
        var result = DateTimeTool.ShiftDateNode(node, string.Empty, string.Empty, null, true);

        // Assert
        resourceNode.InvalidateCaches();
        var updated = resourceNode.ToElement(_schema);
        var updatedNode = updated.Children("birthDate").FirstOrDefault();
        updatedNode?.Value?.ToString().ShouldBe(expectedValue);
        result.WasModified.ShouldBeTrue();
        result.OperationType.ShouldBe(DeIdOperations.Redact);
    }

    [Theory]
    [MemberData(nameof(GetDateTimeDataForRedact))]
    public void GivenADateTime_WhenRedact_ThenDateTimeShouldBeRedacted(string dateTimeValue, string expectedValue)
    {
        // Arrange
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

        // Act
        var result = isDateTime
            ? DateTimeTool.RedactDateTimeAndInstantNode(node, true)
            : DateTimeTool.RedactDateNode(node, true);

        // Assert
        resourceNode.InvalidateCaches();
        var updated = resourceNode.ToElement(_schema);
        var updatedNode = updated.Children(fieldName).FirstOrDefault();
        updatedNode?.Value?.ToString().ShouldBe(expectedValue);
        result.WasModified.ShouldBeTrue();
        result.OperationType.ShouldBe(DeIdOperations.Redact);
    }

    [Theory]
    [MemberData(nameof(GetInstantDataForRedact))]
    public void GivenAnInstant_WhenRedact_ThenInstantShouldBeRedacted(string instantValue, string expectedValue)
    {
        // Arrange
        var json = $$$"""{"resourceType":"Observation","issued":"{{{instantValue}}}"}""";
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);
        var node = element.Children("issued").First();

        // Act
        var result = DateTimeTool.RedactDateTimeAndInstantNode(node, true);

        // Assert
        resourceNode.InvalidateCaches();
        var updated = resourceNode.ToElement(_schema);
        var updatedNode = updated.Children("issued").FirstOrDefault();
        updatedNode?.Value?.ToString().ShouldBe(expectedValue);
        result.WasModified.ShouldBeTrue();
        result.OperationType.ShouldBe(DeIdOperations.Redact);
    }

    [Theory]
    [MemberData(nameof(GetDateTimeDataForDateShiftFormatTest))]
    public void GivenADateTime_WhenDateShift_ThenDateTimeFormatShouldNotChange(string dateShiftKey, string dateTimeValue, string expectedValue)
    {
        // Arrange
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

        // Act
        var result = isDateTime
            ? DateTimeTool.ShiftDateTimeAndInstantNode(node, dateShiftKey, string.Empty, null, true)
            : DateTimeTool.ShiftDateNode(node, dateShiftKey, string.Empty, null, true);

        // Assert
        resourceNode.InvalidateCaches();
        var updated = resourceNode.ToElement(_schema);
        var updatedNode = updated.Children(fieldName).First();
        updatedNode.Value.ToString().ShouldBe(expectedValue);
        result.WasModified.ShouldBeTrue();
        result.OperationType.ShouldBe(DeIdOperations.Perturb);
    }

    [Theory]
    [MemberData(nameof(GetAgeDataForPartialRedact))]
    public void GivenAnAge_WhenPartialRedact_ThenAgeOver89ShouldBeRedacted(int ageValue)
    {
        // Arrange
        var json = $$$"""{"resourceType":"Condition","onsetAge":{"value":{{{ageValue}}},"unit":"a","system":"http://unitsofmeasure.org","code":"a"}}""";
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);
        var node = element.Children("onsetAge").First().Children("value").First();

        // Act
        var result = DateTimeTool.RedactAgeDecimalNode(node, true);

        // Assert
        resourceNode.InvalidateCaches();
        var updated = resourceNode.ToElement(_schema);
        var updatedNode = updated.Children("onsetAge").First().Children("value").FirstOrDefault();
        var expectedValue = ageValue > 89 ? null : ageValue.ToString();
        updatedNode?.Value?.ToString().ShouldBe(expectedValue);
        result.WasModified.ShouldBeTrue();
        result.OperationType.ShouldBe(DeIdOperations.Redact);
    }

    [Theory]
    [MemberData(nameof(GetAgeDataForRedact))]
    public void GivenAnAge_WhenRedact_ThenAgeValueShouldBeNull(int ageValue)
    {
        // Arrange
        var json = $$$"""{"resourceType":"Condition","onsetAge":{"value":{{{ageValue}}},"unit":"a","system":"http://unitsofmeasure.org","code":"a"}}""";
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);
        var node = element.Children("onsetAge").First().Children("value").First();

        // Act
        var result = DateTimeTool.RedactAgeDecimalNode(node, false);

        // Assert
        resourceNode.InvalidateCaches();
        var updated = resourceNode.ToElement(_schema);
        var updatedNode = updated.Children("onsetAge").First().Children("value").FirstOrDefault();
        updatedNode?.Value.ShouldBeNull();
        result.WasModified.ShouldBeTrue();
        result.OperationType.ShouldBe(DeIdOperations.Redact);
    }
}
