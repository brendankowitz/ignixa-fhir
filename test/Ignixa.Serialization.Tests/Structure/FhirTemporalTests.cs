// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.Serialization.Tests;

/// <summary>
/// Tests for <see cref="FhirTemporal"/>, covering the requirement that the type be typed and
/// lossless at the same time.
/// </summary>
public class FhirTemporalTests
{
    [Theory]
    [InlineData("1974", FhirPrimitive.Date, FhirTemporalPrecision.Year)]
    [InlineData("1974-12", FhirPrimitive.Date, FhirTemporalPrecision.Month)]
    [InlineData("1974-12-25", FhirPrimitive.Date, FhirTemporalPrecision.Day)]
    [InlineData("1974-12-25T14:30", FhirPrimitive.DateTime, FhirTemporalPrecision.Minute)]
    [InlineData("1974-12-25T14:30:00", FhirPrimitive.DateTime, FhirTemporalPrecision.Second)]
    [InlineData("1974-12-25T14:30:00.123", FhirPrimitive.DateTime, FhirTemporalPrecision.Millisecond)]
    [InlineData("1974-12-25T14:30:00Z", FhirPrimitive.Instant, FhirTemporalPrecision.Second)]
    [InlineData("1974-12-25T14:30:00.123+10:00", FhirPrimitive.Instant, FhirTemporalPrecision.Millisecond)]
    [InlineData("13:45:00", FhirPrimitive.Time, FhirTemporalPrecision.Second)]
    [InlineData("T13:45:00", FhirPrimitive.Time, FhirTemporalPrecision.Second)]
    public void GivenValidLiteral_WhenParsed_ThenLiteralRoundTripsExactlyAtExpectedPrecision(
        string literal,
        FhirPrimitive kind,
        FhirTemporalPrecision expectedPrecision)
    {
        // Act
        var parsed = FhirTemporal.TryParse(literal, kind, out var result);

        // Assert
        parsed.ShouldBeTrue();
        result.ShouldNotBeNull();
        result.Literal.ShouldBe(literal);
        result.ToString().ShouldBe(literal);
        result.Precision.ShouldBe(expectedPrecision);
        result.Kind.ShouldBe(kind);
    }

    [Fact]
    public void GivenLiteralWithFhirPathSigil_WhenParsed_ThenSigilIsStrippedFromLiteral()
    {
        // Act
        var parsed = FhirTemporal.TryParse("@1974-12-25", FhirPrimitive.Date, out var result);

        // Assert
        parsed.ShouldBeTrue();
        result.ShouldNotBeNull();
        result.Literal.ShouldBe("1974-12-25");
        result.ToString().ShouldBe("1974-12-25");
    }

    [Theory]
    [InlineData("1974", FhirPrimitive.Date)]
    [InlineData("1974-12", FhirPrimitive.Date)]
    [InlineData("13:45:00", FhirPrimitive.Time)]
    public void GivenLiteralWithoutResolvableInstant_WhenParsed_ThenValueIsNull(string literal, FhirPrimitive kind)
    {
        // Act
        FhirTemporal.TryParse(literal, kind, out var result);

        // Assert
        result.ShouldNotBeNull();
        result.Value.ShouldBeNull();
    }

    [Fact]
    public void GivenDayPrecisionLiteral_WhenParsed_ThenValueIsResolved()
    {
        // Act
        FhirTemporal.TryParse("1974-12-25", FhirPrimitive.Date, out var result);

        // Assert
        result.ShouldNotBeNull();
        result.Value.ShouldNotBeNull();
        result.Value.Value.UtcDateTime.ShouldBe(new DateTime(1974, 12, 25, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void GivenSameInstantWrittenInDifferentTimeZones_WhenCompared_ThenValuesAreEqual()
    {
        // Arrange
        FhirTemporal.TryParse("2012-01-01T10:00:00Z", FhirPrimitive.Instant, out var utc);
        FhirTemporal.TryParse("2012-01-01T20:00:00+10:00", FhirPrimitive.Instant, out var offset);

        // Assert
        utc.ShouldNotBeNull();
        offset.ShouldNotBeNull();
        utc.Equals(offset).ShouldBeTrue();
        utc.GetHashCode().ShouldBe(offset.GetHashCode());
        FhirTemporal.Compare(utc, offset).ShouldBe(0);
        utc.Literal.ShouldBe("2012-01-01T10:00:00Z");
        offset.Literal.ShouldBe("2012-01-01T20:00:00+10:00");
    }

    [Fact]
    public void GivenTimeWithAndWithoutLeadingT_WhenCompared_ThenValuesAreEqualAndBothRoundTrip()
    {
        // Arrange
        FhirTemporal.TryParse("13:45:00", FhirPrimitive.Time, out var bare);
        FhirTemporal.TryParse("T13:45:00", FhirPrimitive.Time, out var prefixed);

        // Assert
        bare.ShouldNotBeNull();
        prefixed.ShouldNotBeNull();
        bare.Equals(prefixed).ShouldBeTrue();
        bare.ToString().ShouldBe("13:45:00");
        prefixed.ToString().ShouldBe("T13:45:00");
    }

    [Fact]
    public void GivenTimeAndDateTimeOnTheAnchorDate_WhenCompared_ThenTheyAreNotEqual()
    {
        // Arrange
        FhirTemporal.TryParse("13:45:00", FhirPrimitive.Time, out var time);
        FhirTemporal.TryParse("1900-01-01T13:45:00", FhirPrimitive.DateTime, out var dateTime);

        // Assert
        time.ShouldNotBeNull();
        dateTime.ShouldNotBeNull();
        time.Equals(dateTime).ShouldBeFalse();
        FhirTemporal.Compare(time, dateTime).ShouldBeNull();
    }

    [Theory]
    [InlineData("2012-01-01T00:00:00.0")]
    [InlineData("2012-01-01T00:00:00.000")]
    [InlineData("2012-01-01T00:00:00.0000")]
    public void GivenTrailingZeroFractionalSecond_WhenParsed_ThenItIsEquivalentToTheSecondPrecisionForm(string literal)
    {
        // Arrange
        FhirTemporal.TryParse("2012-01-01T00:00:00", FhirPrimitive.DateTime, out var withoutFraction);
        FhirTemporal.TryParse(literal, FhirPrimitive.DateTime, out var withFraction);

        // Assert
        withFraction.ShouldNotBeNull();
        withoutFraction.ShouldNotBeNull();
        withFraction.Equals(withoutFraction).ShouldBeTrue();
        FhirTemporal.Compare(withFraction, withoutFraction).ShouldBe(0);
        withFraction.Precision.ShouldBe(FhirTemporalPrecision.Second);
        withFraction.Literal.ShouldBe(literal);
    }

    [Fact]
    public void GivenDateTimeAndInstantCarryingTheSameValue_WhenCompared_ThenTheyAreEqual()
    {
        // Arrange
        FhirTemporal.TryParse("2012-01-01T10:00:00Z", FhirPrimitive.DateTime, out var asDateTime);
        FhirTemporal.TryParse("2012-01-01T10:00:00Z", FhirPrimitive.Instant, out var asInstant);

        // Assert
        asDateTime.ShouldNotBeNull();
        asInstant.ShouldNotBeNull();
        asDateTime.Equals(asInstant).ShouldBeTrue();
    }

    [Fact]
    public void GivenValuesOfDifferingPrecisionThatOverlap_WhenCompared_ThenResultIsIndeterminate()
    {
        // Arrange
        FhirTemporal.TryParse("2012", FhirPrimitive.Date, out var year);
        FhirTemporal.TryParse("2012-01", FhirPrimitive.Date, out var month);

        // Act
        var comparison = FhirTemporal.Compare(year, month);

        // Assert
        comparison.ShouldBeNull();
        year.ShouldNotBeNull();
        month.ShouldNotBeNull();
        year.Equals(month).ShouldBeFalse();
    }

    [Fact]
    public void GivenDateAndDateTimeWithinTheSameDay_WhenCompared_ThenResultIsIndeterminate()
    {
        // Arrange
        FhirTemporal.TryParse("2012-01-01", FhirPrimitive.Date, out var date);
        FhirTemporal.TryParse("2012-01-01T10:30:00", FhirPrimitive.DateTime, out var dateTime);

        // Act
        var comparison = FhirTemporal.Compare(date, dateTime);

        // Assert
        comparison.ShouldBeNull();
    }

    [Theory]
    [InlineData("2012", "2013")]
    [InlineData("2012-01", "2012-02")]
    [InlineData("2012-01-01", "2012-01-02")]
    public void GivenNonOverlappingValues_WhenCompared_ThenOrderingIsDeterminate(string earlier, string later)
    {
        // Arrange
        FhirTemporal.TryParse(earlier, FhirPrimitive.Date, out var left);
        FhirTemporal.TryParse(later, FhirPrimitive.Date, out var right);

        // Assert
        FhirTemporal.Compare(left, right).ShouldNotBeNull().ShouldBeLessThan(0);
        FhirTemporal.Compare(right, left).ShouldNotBeNull().ShouldBeGreaterThan(0);
    }

    [Fact]
    public void GivenANullOperand_WhenCompared_ThenResultIsIndeterminate()
    {
        // Arrange
        FhirTemporal.TryParse("2012", FhirPrimitive.Date, out var value);

        // Assert
        FhirTemporal.Compare(value, null).ShouldBeNull();
        FhirTemporal.Compare(null, value).ShouldBeNull();
        FhirTemporal.Compare(null, null).ShouldBeNull();
    }

    [Theory]
    [InlineData(null, FhirPrimitive.Date)]
    [InlineData("", FhirPrimitive.Date)]
    [InlineData("@", FhirPrimitive.Date)]
    [InlineData("abcd", FhirPrimitive.Date)]
    [InlineData("not-a-date", FhirPrimitive.Date)]
    [InlineData("2012-13-45", FhirPrimitive.Date)]
    [InlineData("2012-02-30", FhirPrimitive.Date)]
    [InlineData("2012-01-01T99:99:99", FhirPrimitive.Date)]
    [InlineData("   ", FhirPrimitive.Date)]
    [InlineData(null, FhirPrimitive.Time)]
    [InlineData("abcd", FhirPrimitive.Time)]
    [InlineData(null, FhirPrimitive.DateTime)]
    [InlineData("abcd", FhirPrimitive.DateTime)]
    [InlineData("2012-01-01T99:99:99", FhirPrimitive.DateTime)]
    [InlineData(null, FhirPrimitive.Instant)]
    [InlineData("abcd", FhirPrimitive.Instant)]
    [InlineData("2012-01-01T99:99:99", FhirPrimitive.Instant)]
    public void GivenMalformedLiteral_WhenParsed_ThenParsingFailsWithoutThrowing(string? literal, FhirPrimitive kind)
    {
        // Arrange
        var parsed = false;
        FhirTemporal? result = null;

        // Act
        Should.NotThrow(() => parsed = FhirTemporal.TryParse(literal, kind, out result));

        // Assert
        parsed.ShouldBeFalse();
        result.ShouldBeNull();
    }

    [Theory]
    [InlineData(FhirPrimitive.String)]
    [InlineData(FhirPrimitive.Integer)]
    [InlineData(FhirPrimitive.None)]
    public void GivenNonTemporalKind_WhenParsed_ThenParsingFails(FhirPrimitive kind)
    {
        // Act
        var parsed = FhirTemporal.TryParse("1974-12-25", kind, out var result);

        // Assert
        parsed.ShouldBeFalse();
        result.ShouldBeNull();
    }

    [Fact]
    public void GivenValuesOrderedByInstant_WhenSorted_ThenCompareToProducesChronologicalOrder()
    {
        // Arrange
        FhirTemporal.TryParse("2012-01-01", FhirPrimitive.Date, out var first);
        FhirTemporal.TryParse("2012-06-01", FhirPrimitive.Date, out var second);
        FhirTemporal.TryParse("2013-01-01", FhirPrimitive.Date, out var third);

        List<FhirTemporal> values = [third!, first!, second!];

        // Act
        values.Sort();

        // Assert
        values.Select(value => value.Literal).ShouldBe(["2012-01-01", "2012-06-01", "2013-01-01"]);
    }

    [Fact]
    public void GivenEqualValues_WhenComparedWithCompareTo_ThenResultIsConsistentWithEquals()
    {
        // Arrange
        FhirTemporal.TryParse("2012-01-01T10:00:00Z", FhirPrimitive.Instant, out var utc);
        FhirTemporal.TryParse("2012-01-01T20:00:00+10:00", FhirPrimitive.Instant, out var offset);

        // Assert
        utc.ShouldNotBeNull();
        offset.ShouldNotBeNull();
        utc.CompareTo(offset).ShouldBe(0);
        utc.Equals(offset).ShouldBeTrue();
        (utc == offset).ShouldBeTrue();
        (utc != offset).ShouldBeFalse();
        utc.CompareTo(null).ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData("2012-01-01T10", FhirPrimitive.DateTime)]
    [InlineData("2012-01-01T10", FhirPrimitive.Instant)]
    public void GivenHourPrecisionDateTime_WhenParsed_ThenParsingFails(string literal, FhirPrimitive kind)
    {
        // Act
        var parsed = FhirTemporal.TryParse(literal, kind, out var result);

        // Assert
        parsed.ShouldBeFalse();
        result.ShouldBeNull();
    }

    [Fact]
    public void GivenOffsetBearingDateTime_WhenParsed_ThenValueReflectsCorrectUtcInstant()
    {
        // Act
        FhirTemporal.TryParse("2013-04-02T09:30:10+01:00", FhirPrimitive.DateTime, out var result);

        // Assert
        result.ShouldNotBeNull();
        result.Value.ShouldNotBeNull();
        result.Value.Value.UtcDateTime.ShouldBe(new DateTime(2013, 4, 2, 8, 30, 10, DateTimeKind.Utc));
    }

    [Fact]
    public void GivenOffsetBearingInstant_WhenParsed_ThenValueReflectsCorrectUtcInstant()
    {
        // Act
        FhirTemporal.TryParse("2013-04-02T09:30:10+01:00", FhirPrimitive.Instant, out var result);

        // Assert
        result.ShouldNotBeNull();
        result.Value.ShouldNotBeNull();
        result.Value.Value.UtcDateTime.ShouldBe(new DateTime(2013, 4, 2, 8, 30, 10, DateTimeKind.Utc));
    }

    [Fact]
    public void GivenNonOverlappingTimes_WhenCompared_ThenOrderingIsDeterminate()
    {
        // Arrange
        FhirTemporal.TryParse("13:00:00", FhirPrimitive.Time, out var earlier);
        FhirTemporal.TryParse("14:00:00", FhirPrimitive.Time, out var later);

        // Assert
        earlier.ShouldNotBeNull();
        later.ShouldNotBeNull();
        FhirTemporal.Compare(earlier, later).ShouldNotBeNull().ShouldBeLessThan(0);
        FhirTemporal.Compare(later, earlier).ShouldNotBeNull().ShouldBeGreaterThan(0);
    }

    [Fact]
    public void GivenDifferentValues_WhenCompared_ThenTheyAreNotEqualAndHaveDifferentHashCodes()
    {
        // Arrange
        FhirTemporal.TryParse("2012-01-01", FhirPrimitive.Date, out var a);
        FhirTemporal.TryParse("2013-01-01", FhirPrimitive.Date, out var b);

        // Assert
        a.ShouldNotBeNull();
        b.ShouldNotBeNull();
        a.Equals(b).ShouldBeFalse();
        (a == b).ShouldBeFalse();
        a.GetHashCode().ShouldNotBe(b.GetHashCode());
    }

    [Theory]
    [InlineData("1974", FhirPrimitive.Date, false)]
    [InlineData("1974-12", FhirPrimitive.Date, false)]
    [InlineData("1974-12-25", FhirPrimitive.Date, false)]
    [InlineData("1974-12-25T14:30:00", FhirPrimitive.DateTime, false)]
    [InlineData("1974-12-25T14:30:00Z", FhirPrimitive.DateTime, true)]
    [InlineData("1974-12-25T14:30:00+10:00", FhirPrimitive.DateTime, true)]
    [InlineData("1974-12-25T14:30:00-05:00", FhirPrimitive.DateTime, true)]
    [InlineData("1974-12-25T14:30:00Z", FhirPrimitive.Instant, true)]
    [InlineData("1974-12-25T14:30:00.123+10:00", FhirPrimitive.Instant, true)]
    [InlineData("13:45:00", FhirPrimitive.Time, false)]
    [InlineData("T13:45:00", FhirPrimitive.Time, false)]
    public void GivenLiteral_WhenParsed_ThenHasTimezoneReflectsTheSourceLiteral(
        string literal,
        FhirPrimitive kind,
        bool expectedHasTimezone)
    {
        // Act
        FhirTemporal.TryParse(literal, kind, out var result);

        // Assert
        result.ShouldNotBeNull();
        result.HasTimezone.ShouldBe(expectedHasTimezone);
    }

    [Fact]
    public void GivenTimezoneBearingAndTimezoneLessSameClock_WhenCompared_ThenResultIsIndeterminate()
    {
        // Arrange
        // A fixed UTC instant against a floating local time of the same clock reading: FHIRPath cannot
        // order them because the local time could fall at any offset. This is the exact testEquality23
        // shape and must be empty, not a definite answer.
        FhirTemporal.TryParse("2012-04-15T15:00:00Z", FhirPrimitive.Instant, out var withTimezone);
        FhirTemporal.TryParse("2012-04-15T15:00:00", FhirPrimitive.DateTime, out var withoutTimezone);

        // Act / Assert
        withTimezone.ShouldNotBeNull();
        withoutTimezone.ShouldNotBeNull();
        FhirTemporal.Compare(withTimezone, withoutTimezone).ShouldBeNull();
        FhirTemporal.Compare(withoutTimezone, withTimezone).ShouldBeNull();
        withTimezone.Equals(withoutTimezone).ShouldBeFalse();
    }

    [Fact]
    public void GivenTimezoneBearingAndTimezoneLessDifferentInstants_WhenCompared_ThenResultIsIndeterminate()
    {
        // Arrange
        FhirTemporal.TryParse("2012-04-15T15:00:00Z", FhirPrimitive.Instant, out var withTimezone);
        FhirTemporal.TryParse("2012-04-15T10:00:00", FhirPrimitive.DateTime, out var withoutTimezone);

        // Assert
        withTimezone.ShouldNotBeNull();
        withoutTimezone.ShouldNotBeNull();
        FhirTemporal.Compare(withTimezone, withoutTimezone).ShouldBeNull();
        FhirTemporal.Compare(withoutTimezone, withTimezone).ShouldBeNull();
    }

    [Fact]
    public void GivenTwoTimezoneBearingValuesWithDifferentOffsets_WhenSameInstant_ThenTheyAreEqualAndOrderIsZero()
    {
        // Arrange
        // Both sides carry a timezone, so there is no mismatch: they denote the same fixed instant.
        FhirTemporal.TryParse("2012-04-15T15:00:00Z", FhirPrimitive.Instant, out var utc);
        FhirTemporal.TryParse("2012-04-15T16:00:00+01:00", FhirPrimitive.Instant, out var offset);

        // Assert
        utc.ShouldNotBeNull();
        offset.ShouldNotBeNull();
        utc.HasTimezone.ShouldBeTrue();
        offset.HasTimezone.ShouldBeTrue();
        FhirTemporal.Compare(utc, offset).ShouldBe(0);
        utc.Equals(offset).ShouldBeTrue();
        utc.GetHashCode().ShouldBe(offset.GetHashCode());
    }

    [Fact]
    public void GivenTwoDatePrecisionValues_WhenCompared_ThenTimezonePresenceDoesNotMakeThemIndeterminate()
    {
        // Arrange
        // Neither date carries a timezone (they cannot), so the gate must not fire and their ordering
        // stays determinate.
        FhirTemporal.TryParse("2012-01-01", FhirPrimitive.Date, out var earlier);
        FhirTemporal.TryParse("2012-01-02", FhirPrimitive.Date, out var later);

        // Assert
        earlier.ShouldNotBeNull();
        later.ShouldNotBeNull();
        FhirTemporal.Compare(earlier, later).ShouldNotBeNull().ShouldBeLessThan(0);
    }

    [Fact]
    public void GivenCompareIsZeroAtSecondBoundary_WhenComparedToEquals_ThenInvariantHolds()
    {
        // Arrange
        // Precision >= Second boundary: identical second-precision instants compare zero and are equal;
        // a second vs a non-zero sub-second is a definite non-zero and therefore not equal. Both
        // directions confirm Compare == 0 <=> Equals.
        FhirTemporal.TryParse("2012-04-15T15:00:00Z", FhirPrimitive.Instant, out var second);
        FhirTemporal.TryParse("2012-04-15T15:00:00Z", FhirPrimitive.Instant, out var sameSecond);
        FhirTemporal.TryParse("2012-04-15T15:00:00.5Z", FhirPrimitive.Instant, out var subSecond);

        // Assert
        second.ShouldNotBeNull();
        sameSecond.ShouldNotBeNull();
        subSecond.ShouldNotBeNull();

        FhirTemporal.Compare(second, sameSecond).ShouldBe(0);
        second.Equals(sameSecond).ShouldBeTrue();

        var subSecondOrder = FhirTemporal.Compare(second, subSecond);
        subSecondOrder.ShouldNotBeNull().ShouldBeLessThan(0);
        second.Equals(subSecond).ShouldBeFalse();
    }

    [Fact]
    public void GivenTimeTypedValues_WhenCompared_ThenInvariantHoldsAndTimezoneGateNeverFires()
    {
        // Arrange
        // time values are always timezone-less, so the gate never fires: identical times compare zero
        // and are equal, distinct times order definitely and are not equal.
        FhirTemporal.TryParse("13:45:00", FhirPrimitive.Time, out var time);
        FhirTemporal.TryParse("13:45:00", FhirPrimitive.Time, out var sameTime);
        FhirTemporal.TryParse("14:00:00", FhirPrimitive.Time, out var laterTime);

        // Assert
        time.ShouldNotBeNull();
        sameTime.ShouldNotBeNull();
        laterTime.ShouldNotBeNull();
        time.HasTimezone.ShouldBeFalse();

        FhirTemporal.Compare(time, sameTime).ShouldBe(0);
        time.Equals(sameTime).ShouldBeTrue();

        FhirTemporal.Compare(time, laterTime).ShouldNotBeNull().ShouldBeLessThan(0);
        time.Equals(laterTime).ShouldBeFalse();
    }
}
