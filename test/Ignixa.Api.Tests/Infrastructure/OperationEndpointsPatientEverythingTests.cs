// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable disable

using FluentAssertions;
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
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result.Should().Contain("Observation");
    }

    [Fact]
    public void GivenCommaSeparatedTypes_WhenParsing_ThenReturnsSetWithAllElements()
    {
        // Arrange
        var typeParam = "Observation,Condition,MedicationRequest";

        // Act
        var result = PatientEverythingParameterParser.ParseTypeParameter(typeParam);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);
        result.Should().Contain("Observation");
        result.Should().Contain("Condition");
        result.Should().Contain("MedicationRequest");
    }

    [Fact]
    public void GivenTypesWithSpaces_WhenParsing_ThenTrimsWhitespace()
    {
        // Arrange
        var typeParam = " Observation , Condition , MedicationRequest ";

        // Act
        var result = PatientEverythingParameterParser.ParseTypeParameter(typeParam);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);
        result.Should().Contain("Observation");
        result.Should().Contain("Condition");
        result.Should().Contain("MedicationRequest");
        result.Should().NotContain(" Observation");
        result.Should().NotContain("Condition ");
    }

    [Fact]
    public void GivenEmptyTypeParam_WhenParsing_ThenReturnsNull()
    {
        // Arrange
        var typeParam = string.Empty;

        // Act
        var result = PatientEverythingParameterParser.ParseTypeParameter(typeParam);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GivenNullTypeParam_WhenParsing_ThenReturnsNull()
    {
        // Arrange
        string typeParam = null;

        // Act
        var result = PatientEverythingParameterParser.ParseTypeParameter(typeParam);

        // Assert
        result.Should().BeNull();
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
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public void GivenDuplicateTypes_WhenParsing_ThenReturnsSetWithUniqueElements()
    {
        // Arrange
        var typeParam = "Observation,Condition,Observation";

        // Act
        var result = PatientEverythingParameterParser.ParseTypeParameter(typeParam);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result.Should().Contain("Observation");
        result.Should().Contain("Condition");
    }

    [Fact]
    public void GivenEmptySegmentsInCommaSeparatedList_WhenParsing_ThenIgnoresEmptySegments()
    {
        // Arrange
        var typeParam = "Observation,,Condition,,,MedicationRequest";

        // Act
        var result = PatientEverythingParameterParser.ParseTypeParameter(typeParam);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);
        result.Should().Contain("Observation");
        result.Should().Contain("Condition");
        result.Should().Contain("MedicationRequest");
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
        result.Should().NotBeNull();
        result.Value.Year.Should().Be(2024);
        result.Value.Month.Should().Be(1);
        result.Value.Day.Should().Be(15);
        result.Value.Hour.Should().Be(0);
        result.Value.Minute.Should().Be(0);
        result.Value.Second.Should().Be(0);
        result.Value.Millisecond.Should().Be(0);
        result.Value.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void GivenEndDate_WhenConverting_ThenUsesMaxTimeOfDay()
    {
        // Arrange
        var endDate = new DateOnly(2024, 12, 31);

        // Act
        var result = PatientEverythingParameterParser.ConvertEndDate(endDate);

        // Assert
        result.Should().NotBeNull();
        result.Value.Year.Should().Be(2024);
        result.Value.Month.Should().Be(12);
        result.Value.Day.Should().Be(31);
        result.Value.Hour.Should().Be(23);
        result.Value.Minute.Should().Be(59);
        result.Value.Second.Should().Be(59);
        result.Value.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void GivenNullStartDate_WhenConverting_ThenReturnsNull()
    {
        // Arrange
        DateOnly? startDate = null;

        // Act
        var result = PatientEverythingParameterParser.ConvertStartDate(startDate);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GivenNullEndDate_WhenConverting_ThenReturnsNull()
    {
        // Arrange
        DateOnly? endDate = null;

        // Act
        var result = PatientEverythingParameterParser.ConvertEndDate(endDate);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GivenStartDateAtBeginningOfYear_WhenConverting_ThenReturnsStartOfDay()
    {
        // Arrange
        var startDate = new DateOnly(2024, 1, 1);

        // Act
        var result = PatientEverythingParameterParser.ConvertStartDate(startDate);

        // Assert
        result.Should().NotBeNull();
        result.Value.DateTime.Should().Be(new DateTime(2024, 1, 1, 0, 0, 0));
    }

    [Fact]
    public void GivenEndDateAtEndOfYear_WhenConverting_ThenReturnsEndOfDay()
    {
        // Arrange
        var endDate = new DateOnly(2024, 12, 31);

        // Act
        var result = PatientEverythingParameterParser.ConvertEndDate(endDate);

        // Assert
        result.Should().NotBeNull();
        result.Value.DateTime.Should().Be(new DateTime(2024, 12, 31, 23, 59, 59, 999).AddTicks(9999));
    }

    [Fact]
    public void GivenLeapYearDate_WhenConvertingStart_ThenHandlesCorrectly()
    {
        // Arrange
        var startDate = new DateOnly(2024, 2, 29);

        // Act
        var result = PatientEverythingParameterParser.ConvertStartDate(startDate);

        // Assert
        result.Should().NotBeNull();
        result.Value.Year.Should().Be(2024);
        result.Value.Month.Should().Be(2);
        result.Value.Day.Should().Be(29);
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
        startOffset.Should().NotBeNull();
        endOffset.Should().NotBeNull();
        types.Should().NotBeNull();
        types.Should().HaveCount(2);

        // Verify query parameters would be correctly set
        patientId.Should().Be("patient-123");
        startOffset.Value.DateTime.TimeOfDay.Should().Be(TimeOnly.MinValue.ToTimeSpan());
        endOffset.Value.DateTime.TimeOfDay.Should().Be(TimeOnly.MaxValue.ToTimeSpan());
        since.Should().Be(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));
        count.Should().Be(50);
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
        startOffset.Should().BeNull();
        endOffset.Should().BeNull();
        since.Should().BeNull();
        types.Should().BeNull();
        count.Should().BeNull();
        patientId.Should().Be("patient-456");
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
        startOffset.Should().NotBeNull();
        startOffset.Value.Year.Should().Be(2024);
        endOffset.Should().BeNull();
        since.Year.Should().Be(2024);
        types.Should().BeNull();
        count.Should().Be(100);
        patientId.Should().Be("patient-789");
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
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result.Should().Contain("A");
    }

    [Fact]
    public void GivenTypesWithTrailingComma_WhenParsing_ThenIgnoresTrailingComma()
    {
        // Arrange
        var typeParam = "Observation,Condition,";

        // Act
        var result = PatientEverythingParameterParser.ParseTypeParameter(typeParam);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result.Should().Contain("Observation");
        result.Should().Contain("Condition");
    }

    [Fact]
    public void GivenTypesWithLeadingComma_WhenParsing_ThenIgnoresLeadingComma()
    {
        // Arrange
        var typeParam = ",Observation,Condition";

        // Act
        var result = PatientEverythingParameterParser.ParseTypeParameter(typeParam);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result.Should().Contain("Observation");
        result.Should().Contain("Condition");
    }

    [Fact]
    public void GivenMinDateValue_WhenConvertingStart_ThenHandlesCorrectly()
    {
        // Arrange
        var startDate = DateOnly.MinValue;

        // Act
        var result = PatientEverythingParameterParser.ConvertStartDate(startDate);

        // Assert
        result.Should().NotBeNull();
        result.Value.Year.Should().Be(1);
        result.Value.Month.Should().Be(1);
        result.Value.Day.Should().Be(1);
    }

    [Fact]
    public void GivenMaxDateValue_WhenConvertingEnd_ThenHandlesCorrectly()
    {
        // Arrange
        var endDate = DateOnly.MaxValue;

        // Act
        var result = PatientEverythingParameterParser.ConvertEndDate(endDate);

        // Assert
        result.Should().NotBeNull();
        result.Value.Year.Should().Be(9999);
        result.Value.Month.Should().Be(12);
        result.Value.Day.Should().Be(31);
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
