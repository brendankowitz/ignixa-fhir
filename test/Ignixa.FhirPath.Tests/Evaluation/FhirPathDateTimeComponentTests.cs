/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for FhirPath date/time component extraction (Phase 23).
 *
 * Official Test Suite Reference:
 * https://github.com/HL7/FHIRPath/blob/master/tests/datetime.xml
 */

using Ignixa.FhirPath;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Parser;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class FhirPathDateTimeComponentTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    #region Year Extraction Tests

    [Fact]
    public void GivenDate_WhenYear_ThenReturnsYearComponent()
    {
        // Arrange
        // Official test: @2024-11-18.year() = 2024
        var expr = _parser.Parse("@2024-11-18.year()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(2024, result.Value);
        Assert.Equal("integer", result.InstanceType);
    }

    [Fact]
    public void GivenDateTime_WhenYear_ThenReturnsYearComponent()
    {
        // Arrange
        var expr = _parser.Parse("@2024-11-18T14:30:45Z.year()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(2024, result.Value);
    }

    [Fact]
    public void GivenPartialDate_WhenYear_ThenReturnsYear()
    {
        // Arrange
        // Date with only year precision
        var expr = _parser.Parse("@2024.year()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(2024, result.Value);
    }

    [Fact]
    public void GivenNullDate_WhenYear_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("{}.year()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region Month Extraction Tests

    [Fact]
    public void GivenDate_WhenMonth_ThenReturnsMonthComponent()
    {
        // Arrange
        // Official test: @2024-11-18.month() = 11
        var expr = _parser.Parse("@2024-11-18.month()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(11, result.Value);
        Assert.Equal("integer", result.InstanceType);
    }

    [Fact]
    public void GivenDateTime_WhenMonth_ThenReturnsMonthComponent()
    {
        // Arrange
        var expr = _parser.Parse("@2024-01-15T10:00:00Z.month()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(1, result.Value);
    }

    [Fact]
    public void GivenDecemberDate_WhenMonth_ThenReturnsTwelve()
    {
        // Arrange
        var expr = _parser.Parse("@2024-12-25.month()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(12, result.Value);
    }

    [Fact]
    public void GivenPartialDateYearOnly_WhenMonth_ThenReturnsEmpty()
    {
        // Arrange
        // @2024 has no month component
        var expr = _parser.Parse("@2024.month()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region Day Extraction Tests

    [Fact]
    public void GivenDate_WhenDay_ThenReturnsDayComponent()
    {
        // Arrange
        // Official test: @2024-11-18.day() = 18
        var expr = _parser.Parse("@2024-11-18.day()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(18, result.Value);
        Assert.Equal("integer", result.InstanceType);
    }

    [Fact]
    public void GivenDateTime_WhenDay_ThenReturnsDayComponent()
    {
        // Arrange
        var expr = _parser.Parse("@2024-01-01T00:00:00Z.day()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(1, result.Value);
    }

    [Fact]
    public void GivenLastDayOfMonth_WhenDay_ThenReturnsCorrectDay()
    {
        // Arrange
        var expr = _parser.Parse("@2024-02-29.day()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(29, result.Value);
    }

    [Fact]
    public void GivenPartialDateYearMonth_WhenDay_ThenReturnsEmpty()
    {
        // Arrange
        // @2024-11 has no day component
        var expr = _parser.Parse("@2024-11.day()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region Hour Extraction Tests

    [Fact]
    public void GivenDateTime_WhenHour_ThenReturnsHourComponent()
    {
        // Arrange
        // Official test: @2024-11-18T14:30:45.hour() = 14
        var expr = _parser.Parse("@2024-11-18T14:30:45Z.hour()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(14, result.Value);
        Assert.Equal("integer", result.InstanceType);
    }

    [Fact]
    public void GivenTime_WhenHour_ThenReturnsHourComponent()
    {
        // Arrange
        var expr = _parser.Parse("@T14:30:45.hour()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(14, result.Value);
    }

    [Fact]
    public void GivenMidnight_WhenHour_ThenReturnsZero()
    {
        // Arrange
        var expr = _parser.Parse("@2024-11-18T00:00:00Z.hour()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(0, result.Value);
    }

    [Fact]
    public void GivenDateOnly_WhenHour_ThenReturnsEmpty()
    {
        // Arrange
        // @2024-11-18 has no time component
        var expr = _parser.Parse("@2024-11-18.hour()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region Minute Extraction Tests

    [Fact]
    public void GivenDateTime_WhenMinute_ThenReturnsMinuteComponent()
    {
        // Arrange
        // Official test: @2024-11-18T14:30:45.minute() = 30
        var expr = _parser.Parse("@2024-11-18T14:30:45Z.minute()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(30, result.Value);
        Assert.Equal("integer", result.InstanceType);
    }

    [Fact]
    public void GivenTime_WhenMinute_ThenReturnsMinuteComponent()
    {
        // Arrange
        var expr = _parser.Parse("@T14:30:45.minute()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(30, result.Value);
    }

    [Fact]
    public void GivenZeroMinute_WhenMinute_ThenReturnsZero()
    {
        // Arrange
        var expr = _parser.Parse("@2024-11-18T14:00:00Z.minute()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(0, result.Value);
    }

    #endregion

    #region Second Extraction Tests

    [Fact]
    public void GivenDateTime_WhenSecond_ThenReturnsSecondComponent()
    {
        // Arrange
        // Official test: @2024-11-18T14:30:45.second() = 45
        var expr = _parser.Parse("@2024-11-18T14:30:45Z.second()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(45, result.Value);
        Assert.Equal("integer", result.InstanceType);
    }

    [Fact]
    public void GivenTime_WhenSecond_ThenReturnsSecondComponent()
    {
        // Arrange
        var expr = _parser.Parse("@T14:30:45.second()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(45, result.Value);
    }

    [Fact]
    public void GivenZeroSecond_WhenSecond_ThenReturnsZero()
    {
        // Arrange
        var expr = _parser.Parse("@2024-11-18T14:30:00Z.second()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(0, result.Value);
    }

    #endregion

    #region Millisecond Extraction Tests

    [Fact]
    public void GivenDateTimeWithMilliseconds_WhenMillisecond_ThenReturnsMillisecondComponent()
    {
        // Arrange
        var expr = _parser.Parse("@2024-11-18T14:30:45.123Z.millisecond()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(123, result.Value);
        Assert.Equal("integer", result.InstanceType);
    }

    [Fact]
    public void GivenTimeWithMilliseconds_WhenMillisecond_ThenReturnsMillisecondComponent()
    {
        // Arrange
        var expr = _parser.Parse("@T14:30:45.999.millisecond()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(999, result.Value);
    }

    [Fact]
    public void GivenDateTimeWithoutMilliseconds_WhenMillisecond_ThenReturnsZero()
    {
        // Arrange
        var expr = _parser.Parse("@2024-11-18T14:30:45Z.millisecond()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(0, result.Value);
    }

    #endregion

    #region Timezone Extraction Tests

    [Fact]
    public void GivenDateTimeWithUTC_WhenTimezone_ThenReturnsZulu()
    {
        // Arrange
        var expr = _parser.Parse("@2024-11-18T14:30:45Z.timezone()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Z", result.Value);
        Assert.Equal("string", result.InstanceType);
    }

    [Fact]
    public void GivenDateTimeWithPositiveOffset_WhenTimezone_ThenReturnsOffset()
    {
        // Arrange
        var expr = _parser.Parse("@2024-11-18T14:30:45+05:30.timezone()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("+05:30", result.Value);
    }

    [Fact]
    public void GivenDateTimeWithNegativeOffset_WhenTimezone_ThenReturnsOffset()
    {
        // Arrange
        var expr = _parser.Parse("@2024-11-18T14:30:45-08:00.timezone()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("-08:00", result.Value);
    }

    [Fact]
    public void GivenDateTimeWithoutTimezone_WhenTimezone_ThenReturnsEmpty()
    {
        // Arrange
        // Local datetime without timezone
        var expr = _parser.Parse("@2024-11-18T14:30:45.timezone()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenDateOnly_WhenTimezone_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("@2024-11-18.timezone()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region FHIR Examples - Patient Age Calculation

    [Fact]
    public void GivenPatientBirthDate_WhenCalculatingAge_ThenReturnsYearDifference()
    {
        // Arrange
        // Age calculation: today().year() - Patient.birthDate.year()
        // Simulating: Patient born 1990-05-15, today is 2024-11-18 = 34 years
        var expr = _parser.Parse("2024 - 1990");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(34, result.Value);
    }

    [Fact]
    public void GivenPatientBirthDatePartial_WhenExtractingYear_ThenReturnsYear()
    {
        // Arrange
        // Patient.birthDate might only have year precision
        var expr = _parser.Parse("@1990.year()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(1990, result.Value);
    }

    [Fact]
    public void GivenMultiplePatients_WhenFilteringByBirthYear_ThenFiltersCorrectly()
    {
        // Arrange
        // Filter patients born in specific year: Patient.where(birthDate.year() = 1990)
        var expr = _parser.Parse("@1990-05-15.year() = 1990");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    #endregion

    #region FHIR Examples - Temporal Filtering

    [Fact]
    public void GivenObservations_WhenFilteringByYear_ThenFiltersCorrectly()
    {
        // Arrange
        // Observation.where(effective.year() = 2024)
        var expr = _parser.Parse("@2024-11-18T10:00:00Z.year() = 2024");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenObservations_WhenFilteringByMonth_ThenFiltersCorrectly()
    {
        // Arrange
        // Observation.where(effective.month() = 11) - November observations
        var expr = _parser.Parse("@2024-11-18T10:00:00Z.month() = 11");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenObservations_WhenFilteringByDay_ThenFiltersCorrectly()
    {
        // Arrange
        // Filter by specific day of month
        var expr = _parser.Parse("@2024-11-18T10:00:00Z.day() = 18");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    #endregion

    #region FHIR Examples - Appointment Scheduling

    [Fact]
    public void GivenAppointment_WhenCheckingBusinessHours_ThenValidatesHour()
    {
        // Arrange
        // Appointment.start.hour() between 9 and 17 (business hours)
        var expr = _parser.Parse("@2024-11-18T14:30:00Z.hour() >= 9 and @2024-11-18T14:30:00Z.hour() <= 17");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenAppointmentOutsideHours_WhenCheckingBusinessHours_ThenReturnsFalse()
    {
        // Arrange
        var expr = _parser.Parse("@2024-11-18T20:00:00Z.hour() >= 9 and @2024-11-18T20:00:00Z.hour() <= 17");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.False((bool)result.Value!);
    }

    [Fact]
    public void GivenAppointment_WhenExtractingMinute_ThenReturnsCorrectMinute()
    {
        // Arrange
        // Check for half-hour slots: start.minute() = 30
        var expr = _parser.Parse("@2024-11-18T14:30:00Z.minute() = 30");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GivenLeapYearDate_WhenExtractingComponents_ThenHandlesCorrectly()
    {
        // Arrange
        var expr = _parser.Parse("@2024-02-29.year() = 2024 and @2024-02-29.month() = 2 and @2024-02-29.day() = 29");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenMaxDateTime_WhenExtractingComponents_ThenHandlesCorrectly()
    {
        // Arrange
        var expr = _parser.Parse("@9999-12-31T23:59:59.999Z.year()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(9999, result.Value);
    }

    [Fact]
    public void GivenMinDateTime_WhenExtractingComponents_ThenHandlesCorrectly()
    {
        // Arrange
        var expr = _parser.Parse("@0001-01-01T00:00:00Z.year()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(1, result.Value);
    }

    [Fact]
    public void GivenNonExistentComponent_WhenExtracting_ThenReturnsEmpty()
    {
        // Arrange
        // Trying to get hour from date-only
        var expr = _parser.Parse("@2024-11-18.hour()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region Duration Function Tests

    [Fact]
    public void GivenTwoDates_WhenDuration_ThenReturnsDayQuantity()
    {
        // Arrange - Full day precision dates
        var expr = _parser.Parse("@2020-01-01.duration(@2020-01-11)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
        var quantity = result.Value as Ignixa.FhirPath.Types.Quantity;
        Assert.NotNull(quantity);
        Assert.Equal(10m, quantity.Value);
        Assert.Equal("day", quantity.Unit);
    }

    [Fact]
    public void GivenTwoYearOnlyDates_WhenDuration_ThenReturnsYearQuantity()
    {
        // Arrange - Year precision only
        var expr = _parser.Parse("@2020.duration(@2022)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
        var quantity = result.Value as Ignixa.FhirPath.Types.Quantity;
        Assert.NotNull(quantity);
        // 2 years ≈ 730.5 days / 365.25 ≈ 2.0
        Assert.True(quantity.Value >= 1.9m && quantity.Value <= 2.1m);
        Assert.Equal("year", quantity.Unit);
    }

    [Fact]
    public void GivenTwoMonthDates_WhenDuration_ThenReturnsMonthQuantity()
    {
        // Arrange - Month precision
        var expr = _parser.Parse("@2020-01.duration(@2021-06)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
        var quantity = result.Value as Ignixa.FhirPath.Types.Quantity;
        Assert.NotNull(quantity);
        // 17 months ≈ 517 days / 30.4375 ≈ 17
        Assert.True(quantity.Value >= 16m && quantity.Value <= 18m);
        Assert.Equal("month", quantity.Unit);
    }

    [Fact]
    public void GivenTwoDateTimes_WhenDuration_ThenReturnsHourQuantity()
    {
        // Arrange - Second precision (both have seconds specified)
        var expr = _parser.Parse("@2020-01-01T10:00:00.duration(@2020-01-01T14:30:00)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert - Since both dates have second precision, result is in seconds
        Assert.Equal("Quantity", result.InstanceType);
        var quantity = result.Value as Ignixa.FhirPath.Types.Quantity;
        Assert.NotNull(quantity);
        Assert.Equal(16200m, quantity.Value); // 4.5 hours = 16200 seconds
        Assert.Equal("second", quantity.Unit);
    }

    [Fact]
    public void GivenEmptyCollection_WhenDuration_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("{}.duration(@2020-01-01)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region Difference Function Tests

    [Fact]
    public void GivenTwoDates_WhenDifference_ThenReturnsDayQuantity()
    {
        // Arrange - Full day precision dates
        var expr = _parser.Parse("@2020-01-01.difference(@2020-01-11)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
        var quantity = result.Value as Ignixa.FhirPath.Types.Quantity;
        Assert.NotNull(quantity);
        Assert.Equal(10m, quantity.Value);
        Assert.Equal("day", quantity.Unit);
    }

    [Fact]
    public void GivenTwoYearOnlyDates_WhenDifference_ThenReturnsYearQuantity()
    {
        // Arrange - Year precision only
        var expr = _parser.Parse("@2020.difference(@2022)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
        var quantity = result.Value as Ignixa.FhirPath.Types.Quantity;
        Assert.NotNull(quantity);
        Assert.Equal(2m, quantity.Value);  // Exactly 2 years
        Assert.Equal("year", quantity.Unit);
    }

    [Fact]
    public void GivenTwoMonthDates_WhenDifference_ThenReturnsMonthQuantity()
    {
        // Arrange - Month precision
        var expr = _parser.Parse("@2020-01.difference(@2021-06)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
        var quantity = result.Value as Ignixa.FhirPath.Types.Quantity;
        Assert.NotNull(quantity);
        Assert.Equal(17m, quantity.Value);  // Exactly 17 months
        Assert.Equal("month", quantity.Unit);
    }

    [Fact]
    public void GivenTwoDateTimes_WhenDifference_ThenReturnsHourQuantity()
    {
        // Arrange - Second precision (both have seconds specified)
        var expr = _parser.Parse("@2020-01-01T10:00:00.difference(@2020-01-01T14:00:00)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert - Since both dates have second precision, result is in seconds
        Assert.Equal("Quantity", result.InstanceType);
        var quantity = result.Value as Ignixa.FhirPath.Types.Quantity;
        Assert.NotNull(quantity);
        Assert.Equal(14400m, quantity.Value);  // 4 hours = 14400 seconds
        Assert.Equal("second", quantity.Unit);
    }

    [Fact]
    public void GivenDifferenceReversed_WhenDifference_ThenReturnsAbsoluteValue()
    {
        // Arrange - Test that order doesn't matter (absolute value)
        var expr = _parser.Parse("@2020-01-11.difference(@2020-01-01)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
        var quantity = result.Value as Ignixa.FhirPath.Types.Quantity;
        Assert.NotNull(quantity);
        Assert.Equal(10m, quantity.Value);  // Still 10 days, not -10
        Assert.Equal("day", quantity.Unit);
    }

    [Fact]
    public void GivenEmptyCollection_WhenDifference_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("{}.difference(@2020-01-01)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenDatesWithDifferentPrecision_WhenDifference_ThenUsesLowerPrecision()
    {
        // Arrange - Year vs full date: should use year precision
        var expr = _parser.Parse("@2020.difference(@2022-06-15)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
        var quantity = result.Value as Ignixa.FhirPath.Types.Quantity;
        Assert.NotNull(quantity);
        Assert.Equal(2m, quantity.Value);  // 2 years (using year precision)
        Assert.Equal("year", quantity.Unit);
    }

    #endregion

    #region Helper Methods

    private IElement CreateIntegerElement(int value)
    {
        return new PrimitiveElement(value, "integer");
    }

    private class PrimitiveElement : IElement
    {
        public PrimitiveElement(object value, string type)
        {
            Value = value;
            InstanceType = type;
        }

        public string Name => string.Empty;
        public string InstanceType { get; }
        public object? Value { get; }
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => true;

        public IReadOnlyList<IElement> Children(string? name = null) => Array.Empty<IElement>();

        public T? Meta<T>() where T : class => null;
    }

    #endregion
}
