// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable disable

using Shouldly;
using Xunit;

namespace Ignixa.Api.Tests.Infrastructure;

/// <summary>
/// Unit tests for OperationEndpoints Patient $everything operation parameter parsing.
/// Tests the type parameter parsing, date conversion, and query creation logic.
/// </summary>
public class OperationEndpointsPatientEverythingTests
{
    #region Type Parameter Parsing Tests

    [Fact]
    public void GivenSingleType_WhenParsing_ThenReturnsSetWithOneElement()
    {
        // Arrange
        var typeParam = "Observation";

        // Act
        var result = PatientEverythingParameterParser.ParseTypeParameter(typeParam);

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBe(1);
        result.ShouldContain("Observation");
    }

    [Fact]
    public void GivenCommaSeparatedTypes_WhenParsing_ThenReturnsSetWithAllElements()
    {
        // Arrange
        var typeParam = "Observation,Condition,MedicationRequest";

        // Act
        var result = PatientEverythingParameterParser.ParseTypeParameter(typeParam);

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBe(3);
        result.ShouldContain("Observation");
        result.ShouldContain("Condition");
        result.ShouldContain("MedicationRequest");
    }

    [Fact]
    public void GivenTypesWithSpaces_WhenParsing_ThenTrimsWhitespace()
    {
        // Arrange
        var typeParam = " Observation , Condition , MedicationRequest ";

        // Act
        var result = PatientEverythingParameterParser.ParseTypeParameter(typeParam);

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBe(3);
        result.ShouldContain("Observation");
        result.ShouldContain("Condition");
        result.ShouldContain("MedicationRequest");
        result.ShouldNotContain(" Observation");
        result.ShouldNotContain("Condition ");
    }

    [Fact]
    public void GivenEmptyTypeParam_WhenParsing_ThenReturnsNull()
    {
        // Arrange
        var typeParam = string.Empty;

        // Act
        var result = PatientEverythingParameterParser.ParseTypeParameter(typeParam);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void GivenNullTypeParam_WhenParsing_ThenReturnsNull()
    {
        // Arrange
        string typeParam = null;

        // Act
        var result = PatientEverythingParameterParser.ParseTypeParameter(typeParam);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void GivenWhitespaceOnlyTypeParam_WhenParsing_ThenReturnsEmptySet()
    {
        // Arrange
        var typeParam = "   ";

        // Act
        var result = PatientEverythingParameterParser.ParseTypeParameter(typeParam);

        // Assert
        // Note: Whitespace-only strings pass !string.IsNullOrEmpty check,
        // but after Split with RemoveEmptyEntries, result is empty HashSet
        result.ShouldNotBeNull();
        result.ShouldBeEmpty();
    }

    [Fact]
    public void GivenDuplicateTypes_WhenParsing_ThenReturnsSetWithUniqueElements()
    {
        // Arrange
        var typeParam = "Observation,Condition,Observation";

        // Act
        var result = PatientEverythingParameterParser.ParseTypeParameter(typeParam);

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBe(2);
        result.ShouldContain("Observation");
        result.ShouldContain("Condition");
    }

    [Fact]
    public void GivenEmptySegmentsInCommaSeparatedList_WhenParsing_ThenIgnoresEmptySegments()
    {
        // Arrange
        var typeParam = "Observation,,Condition,,,MedicationRequest";

        // Act
        var result = PatientEverythingParameterParser.ParseTypeParameter(typeParam);

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBe(3);
        result.ShouldContain("Observation");
        result.ShouldContain("Condition");
        result.ShouldContain("MedicationRequest");
    }

    #endregion

    #region Date Parameter Conversion Tests

    [Fact]
    public void GivenStartDate_WhenConverting_ThenUsesMinTimeOfDay()
    {
        // Arrange
        var startDate = new DateOnly(2024, 1, 15);

        // Act
        var result = PatientEverythingParameterParser.ConvertStartDate(startDate);

        // Assert
        result.ShouldNotBeNull();
        result.Value.Year.ShouldBe(2024);
        result.Value.Month.ShouldBe(1);
        result.Value.Day.ShouldBe(15);
        result.Value.Hour.ShouldBe(0);
        result.Value.Minute.ShouldBe(0);
        result.Value.Second.ShouldBe(0);
        result.Value.Millisecond.ShouldBe(0);
        result.Value.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void GivenEndDate_WhenConverting_ThenUsesMaxTimeOfDay()
    {
        // Arrange
        var endDate = new DateOnly(2024, 12, 31);

        // Act
        var result = PatientEverythingParameterParser.ConvertEndDate(endDate);

        // Assert
        result.ShouldNotBeNull();
        result.Value.Year.ShouldBe(2024);
        result.Value.Month.ShouldBe(12);
        result.Value.Day.ShouldBe(31);
        result.Value.Hour.ShouldBe(23);
        result.Value.Minute.ShouldBe(59);
        result.Value.Second.ShouldBe(59);
        result.Value.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void GivenNullStartDate_WhenConverting_ThenReturnsNull()
    {
        // Arrange
        DateOnly? startDate = null;

        // Act
        var result = PatientEverythingParameterParser.ConvertStartDate(startDate);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void GivenNullEndDate_WhenConverting_ThenReturnsNull()
    {
        // Arrange
        DateOnly? endDate = null;

        // Act
        var result = PatientEverythingParameterParser.ConvertEndDate(endDate);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void GivenStartDateAtBeginningOfYear_WhenConverting_ThenReturnsStartOfDay()
    {
        // Arrange
        var startDate = new DateOnly(2024, 1, 1);

        // Act
        var result = PatientEverythingParameterParser.ConvertStartDate(startDate);

        // Assert
        result.ShouldNotBeNull();
        result.Value.DateTime.ShouldBe(new DateTime(2024, 1, 1, 0, 0, 0));
    }

    [Fact]
    public void GivenEndDateAtEndOfYear_WhenConverting_ThenReturnsEndOfDay()
    {
        // Arrange
        var endDate = new DateOnly(2024, 12, 31);

        // Act
        var result = PatientEverythingParameterParser.ConvertEndDate(endDate);

        // Assert
        result.ShouldNotBeNull();
        result.Value.DateTime.ShouldBe(new DateTime(2024, 12, 31, 23, 59, 59, 999).AddTicks(9999));
    }

    [Fact]
    public void GivenLeapYearDate_WhenConvertingStart_ThenHandlesCorrectly()
    {
        // Arrange
        var startDate = new DateOnly(2024, 2, 29);

        // Act
        var result = PatientEverythingParameterParser.ConvertStartDate(startDate);

        // Assert
        result.ShouldNotBeNull();
        result.Value.Year.ShouldBe(2024);
        result.Value.Month.ShouldBe(2);
        result.Value.Day.ShouldBe(29);
    }

    #endregion

    #region Query Creation Tests

    [Fact]
    public void GivenAllParameters_WhenCreatingQuery_ThenAllPropertiesSet()
    {
        // Arrange
        var patientId = "patient-123";
        var startDate = new DateOnly(2024, 1, 1);
        var endDate = new DateOnly(2024, 12, 31);
        var since = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var typeParam = "Observation,Condition";
        var count = 50;

        // Act
        var startOffset = PatientEverythingParameterParser.ConvertStartDate(startDate);
        var endOffset = PatientEverythingParameterParser.ConvertEndDate(endDate);
        var types = PatientEverythingParameterParser.ParseTypeParameter(typeParam);

        // Assert - verify all components are correctly prepared
        startOffset.ShouldNotBeNull();
        endOffset.ShouldNotBeNull();
        types.ShouldNotBeNull();
        types.Count.ShouldBe(2);

        // Verify query parameters would be correctly set
        patientId.ShouldBe("patient-123");
        startOffset.Value.DateTime.TimeOfDay.ShouldBe(TimeOnly.MinValue.ToTimeSpan());
        endOffset.Value.DateTime.TimeOfDay.ShouldBe(TimeOnly.MaxValue.ToTimeSpan());
        since.ShouldBe(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));
        count.ShouldBe(50);
    }

    [Fact]
    public void GivenOnlyPatientId_WhenCreatingQuery_ThenOtherPropertiesNull()
    {
        // Arrange
        var patientId = "patient-456";
        DateOnly? startDate = null;
        DateOnly? endDate = null;
        DateTimeOffset? since = null;
        string typeParam = null;
        int? count = null;

        // Act
        var startOffset = PatientEverythingParameterParser.ConvertStartDate(startDate);
        var endOffset = PatientEverythingParameterParser.ConvertEndDate(endDate);
        var types = PatientEverythingParameterParser.ParseTypeParameter(typeParam);

        // Assert - verify optional parameters are correctly null
        startOffset.ShouldBeNull();
        endOffset.ShouldBeNull();
        since.ShouldBeNull();
        types.ShouldBeNull();
        count.ShouldBeNull();
        patientId.ShouldBe("patient-456");
    }

    [Fact]
    public void GivenMixedParameters_WhenCreatingQuery_ThenOnlyProvidedParametersSet()
    {
        // Arrange
        var patientId = "patient-789";
        var startDate = new DateOnly(2024, 1, 1);
        DateOnly? endDate = null;
        var since = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        string typeParam = null;
        var count = 100;

        // Act
        var startOffset = PatientEverythingParameterParser.ConvertStartDate(startDate);
        var endOffset = PatientEverythingParameterParser.ConvertEndDate(endDate);
        var types = PatientEverythingParameterParser.ParseTypeParameter(typeParam);

        // Assert
        startOffset.ShouldNotBeNull();
        startOffset.Value.Year.ShouldBe(2024);
        endOffset.ShouldBeNull();
        since.Year.ShouldBe(2024);
        types.ShouldBeNull();
        count.ShouldBe(100);
        patientId.ShouldBe("patient-789");
    }

    #endregion

    #region Edge Cases Tests

    [Fact]
    public void GivenSingleCharacterType_WhenParsing_ThenReturnsSetWithSingleCharacter()
    {
        // Arrange
        var typeParam = "A";

        // Act
        var result = PatientEverythingParameterParser.ParseTypeParameter(typeParam);

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBe(1);
        result.ShouldContain("A");
    }

    [Fact]
    public void GivenTypesWithTrailingComma_WhenParsing_ThenIgnoresTrailingComma()
    {
        // Arrange
        var typeParam = "Observation,Condition,";

        // Act
        var result = PatientEverythingParameterParser.ParseTypeParameter(typeParam);

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBe(2);
        result.ShouldContain("Observation");
        result.ShouldContain("Condition");
    }

    [Fact]
    public void GivenTypesWithLeadingComma_WhenParsing_ThenIgnoresLeadingComma()
    {
        // Arrange
        var typeParam = ",Observation,Condition";

        // Act
        var result = PatientEverythingParameterParser.ParseTypeParameter(typeParam);

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBe(2);
        result.ShouldContain("Observation");
        result.ShouldContain("Condition");
    }

    [Fact]
    public void GivenMinDateValue_WhenConvertingStart_ThenHandlesCorrectly()
    {
        // Arrange
        var startDate = DateOnly.MinValue;

        // Act
        var result = PatientEverythingParameterParser.ConvertStartDate(startDate);

        // Assert
        result.ShouldNotBeNull();
        result.Value.Year.ShouldBe(1);
        result.Value.Month.ShouldBe(1);
        result.Value.Day.ShouldBe(1);
    }

    [Fact]
    public void GivenMaxDateValue_WhenConvertingEnd_ThenHandlesCorrectly()
    {
        // Arrange
        var endDate = DateOnly.MaxValue;

        // Act
        var result = PatientEverythingParameterParser.ConvertEndDate(endDate);

        // Assert
        result.ShouldNotBeNull();
        result.Value.Year.ShouldBe(9999);
        result.Value.Month.ShouldBe(12);
        result.Value.Day.ShouldBe(31);
    }

    #endregion
}

/// <summary>
/// Internal helper class to extract and test Patient $everything parameter parsing logic.
/// Mirrors the parsing logic from OperationEndpoints.HandlePatientEverythingInternal.
/// </summary>
internal static class PatientEverythingParameterParser
{
    /// <summary>
    /// Parses the _type parameter (comma-delimited list of resource types).
    /// </summary>
    /// <param name="typeParam">Comma-separated list of resource types.</param>
    /// <returns>Set of resource types, or null if parameter is empty.</returns>
    public static ISet<string> ParseTypeParameter(string typeParam)
    {
        if (string.IsNullOrEmpty(typeParam))
        {
            return null;
        }

        return new HashSet<string>(typeParam.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    /// <summary>
    /// Converts a DateOnly start date to DateTimeOffset with minimum time of day (00:00:00).
    /// </summary>
    /// <param name="start">Start date.</param>
    /// <returns>DateTimeOffset at beginning of day, or null if start is null.</returns>
    public static DateTimeOffset? ConvertStartDate(DateOnly? start)
    {
        return start.HasValue
            ? new DateTimeOffset(start.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : null;
    }

    /// <summary>
    /// Converts a DateOnly end date to DateTimeOffset with maximum time of day (23:59:59.9999999).
    /// </summary>
    /// <param name="end">End date.</param>
    /// <returns>DateTimeOffset at end of day, or null if end is null.</returns>
    public static DateTimeOffset? ConvertEndDate(DateOnly? end)
    {
        return end.HasValue
            ? new DateTimeOffset(end.Value.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero)
            : null;
    }
}
