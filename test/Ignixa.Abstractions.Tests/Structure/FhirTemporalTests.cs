// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Globalization;

namespace Ignixa.Abstractions.Tests;

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

    [Theory]
    [InlineData(FhirPrimitive.DateTime)]
    [InlineData(FhirPrimitive.Instant)]
    public void GivenAnOffsetBearingLiteral_WhenComparedToTheSameInstantWrittenInUtc_ThenTheyAgree(FhirPrimitive kind)
    {
        // The offset has to be applied when the ordering keys are derived, not just displayed: +01:00 at
        // 09:30:10 is 08:30:10Z, so the two literals are one instant and must be indistinguishable to
        // equality and ordering alike.

        // Act
        FhirTemporal.TryParse("2013-04-02T09:30:10+01:00", kind, out var offsetBearing);
        FhirTemporal.TryParse("2013-04-02T08:30:10Z", kind, out var utc);

        // Assert
        offsetBearing.ShouldNotBeNull();
        utc.ShouldNotBeNull();
        offsetBearing.Equals(utc).ShouldBeTrue();
        offsetBearing.CompareTo(utc).ShouldBe(0);
        offsetBearing.Literal.ShouldBe("2013-04-02T09:30:10+01:00");
    }

    [Theory]
    [InlineData(FhirPrimitive.DateTime)]
    [InlineData(FhirPrimitive.Instant)]
    public void GivenAnOffsetBearingLiteral_WhenComparedToADifferentInstant_ThenTheOffsetIsNotIgnored(FhirPrimitive kind)
    {
        // Guards the above against passing for the wrong reason: if the offset were dropped rather than
        // applied, 09:30:10+01:00 would compare equal to 09:30:10Z instead of to 08:30:10Z.

        // Act
        FhirTemporal.TryParse("2013-04-02T09:30:10+01:00", kind, out var offsetBearing);
        FhirTemporal.TryParse("2013-04-02T09:30:10Z", kind, out var sameClockDifferentInstant);

        // Assert
        offsetBearing.ShouldNotBeNull();
        sameClockDifferentInstant.ShouldNotBeNull();
        offsetBearing.Equals(sameClockDifferentInstant).ShouldBeFalse();
        offsetBearing.CompareTo(sameClockDifferentInstant).ShouldBeLessThan(0);
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

    // -----------------------------------------------------------------------------------------
    // GetLiteralPrecision, IsTemporalLiteral, IsDateOrDateTimeLiteral — shape-classification
    // heuristics. These are pinned here because the contract deliberately diverges from TryParse
    // in ways that are easy to mistake for a bug (see F4 remarks).
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void GivenHourOnlyDateTime_WhenGetLiteralPrecision_ThenReturnsHourEvenThoughTryParseRejects()
    {
        // GetLiteralPrecision classifies shape only — hour-only is a recognisable shape.
        FhirTemporal.GetLiteralPrecision("2012-01-01T10").ShouldBe(FhirTemporalPrecision.Hour);

        // TryParse enforces FHIR validity — hour-only dateTime is not valid FHIR, so it fails.
        FhirTemporal.TryParse("2012-01-01T10", FhirPrimitive.DateTime, out _).ShouldBeFalse();
    }

    [Fact]
    public void GivenFhirPathSigilPrefix_WhenGetLiteralPrecision_ThenSigilIsStrippedBeforeClassification()
    {
        FhirTemporal.GetLiteralPrecision("@2012").ShouldBe(FhirTemporalPrecision.Year);
        FhirTemporal.GetLiteralPrecision("@2012-01-01").ShouldBe(FhirTemporalPrecision.Day);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("@")]
    public void GivenNullOrEmptyOrSigilOnlyLiteral_WhenGetLiteralPrecision_ThenReturnsInvalid(string? literal)
    {
        FhirTemporal.GetLiteralPrecision(literal).ShouldBe(FhirTemporalPrecision.Invalid);
    }

    [Theory]
    [InlineData("2012", FhirTemporalPrecision.Year)]
    [InlineData("2012-01", FhirTemporalPrecision.Month)]
    [InlineData("2012-01-01", FhirTemporalPrecision.Day)]
    [InlineData("2012-01-01T10:30", FhirTemporalPrecision.Minute)]
    [InlineData("2012-01-01T10:30:00", FhirTemporalPrecision.Second)]
    [InlineData("2012-01-01T10:30:00.123", FhirTemporalPrecision.Millisecond)]
    [InlineData("T10:30", FhirTemporalPrecision.Minute)]
    [InlineData("T13:45:00", FhirTemporalPrecision.Second)]
    public void GivenTemporalLiteralShape_WhenGetLiteralPrecision_ThenReturnsExpectedPrecision(
        string literal,
        FhirTemporalPrecision expected)
    {
        FhirTemporal.GetLiteralPrecision(literal).ShouldBe(expected);
    }

    [Theory]
    [InlineData("T10:30", false)]
    [InlineData("13:45:00", false)]
    [InlineData("2012", true)]
    [InlineData("2012-01-01", true)]
    [InlineData("2012-01-01T10:30", true)]
    [InlineData("@2012-01-01", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("abc", false)]
    public void GivenLiteral_WhenIsDateOrDateTimeLiteral_ThenTimeLiteralsReturnFalseAndDateLiteralsReturnTrue(
        string? literal,
        bool expected)
    {
        FhirTemporal.IsDateOrDateTimeLiteral(literal).ShouldBe(expected);
    }

    [Theory]
    [InlineData("@2012", true)]
    [InlineData("T10:30", true)]
    [InlineData("2012-01-01", true)]
    [InlineData("2012-01-01T10:30", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("abc", false)]
    public void GivenLiteral_WhenIsTemporalLiteral_ThenAtPrefixedTAndYearFirstLiteralsReturnTrue(
        string? literal,
        bool expected)
    {
        FhirTemporal.IsTemporalLiteral(literal).ShouldBe(expected);
    }

    [Theory]
    [InlineData("2024-02-29", FhirPrimitive.Date)]
    [InlineData("2000-02-29", FhirPrimitive.Date)]
    [InlineData("2024-02-29T10:30:00Z", FhirPrimitive.Instant)]
    public void GivenLeapDayInALeapYear_WhenParsed_ThenItIsAccepted(string literal, FhirPrimitive kind)
    {
        // Act
        var parsed = FhirTemporal.TryParse(literal, kind, out var result);

        // Assert
        parsed.ShouldBeTrue();
        result.ShouldNotBeNull();
        result.Literal.ShouldBe(literal);
        result.ToString().ShouldBe(literal);
    }

    [Fact]
    public void GivenLeapDay_WhenComparedToAdjacentDates_ThenItOrdersBetweenThem()
    {
        // The bounds FhirTemporal resolves a literal to are private, so acceptance
        // (ShouldBeTrue above) does not prove "2024-02-29" resolved to the calendar date
        // February 29th rather than merely being accepted as a well-formed literal. Ordering
        // against its immediate neighbours is the observable substitute: only a leap-day-correct
        // parse sorts strictly after Feb 28 and strictly before Mar 1.
        FhirTemporal.TryParse("2024-02-28", FhirPrimitive.Date, out var feb28).ShouldBeTrue();
        FhirTemporal.TryParse("2024-02-29", FhirPrimitive.Date, out var feb29).ShouldBeTrue();
        FhirTemporal.TryParse("2024-03-01", FhirPrimitive.Date, out var mar01).ShouldBeTrue();

        feb29!.CompareTo(feb28).ShouldBeGreaterThan(0);
        feb29.CompareTo(mar01).ShouldBeLessThan(0);
    }

    [Theory]
    [InlineData("2023-02-29", FhirPrimitive.Date)]
    [InlineData("1900-02-29", FhirPrimitive.Date)]
    [InlineData("2023-02-29T10:30:00Z", FhirPrimitive.Instant)]
    public void GivenLeapDayInANonLeapYear_WhenParsed_ThenParsingFails(string literal, FhirPrimitive kind)
    {
        // 1900 is the century rule: divisible by 100 but not 400, so it is not a leap year. Both this and
        // the plain 2023 case are rejected because the bounds are resolved through DateTimeOffset.TryParse,
        // which enforces the calendar. Nothing silently accepts an impossible date.

        // Act
        var parsed = FhirTemporal.TryParse(literal, kind, out var result);

        // Assert
        parsed.ShouldBeFalse();
        result.ShouldBeNull();
    }

    [Theory]
    [InlineData(FhirPrimitive.Date)]
    [InlineData(FhirPrimitive.DateTime)]
    [InlineData(FhirPrimitive.Instant)]
    [InlineData(FhirPrimitive.Time)]
    public void GivenNullLiteral_WhenParsed_ThenParsingFailsForEveryTemporalKind(FhirPrimitive kind)
    {
        // Act
        var parsed = FhirTemporal.TryParse(null, kind, out var result);

        // Assert
        parsed.ShouldBeFalse();
        result.ShouldBeNull();
    }

    [Theory]
    [InlineData(FhirPrimitive.Date)]
    [InlineData(FhirPrimitive.DateTime)]
    [InlineData(FhirPrimitive.Instant)]
    [InlineData(FhirPrimitive.Time)]
    public void GivenEmptyLiteral_WhenParsed_ThenParsingFailsForEveryTemporalKind(FhirPrimitive kind)
    {
        // Act
        var parsed = FhirTemporal.TryParse(string.Empty, kind, out var result);

        // Assert
        parsed.ShouldBeFalse();
        result.ShouldBeNull();
    }

    [Fact]
    public void GivenTimezoneBearingAndTimezoneLessSameClock_WhenOrderedWithCompareTo_ThenTheyDoNotCollide()
    {
        // The CompareTo/Equals consistency contract. These two differ only in timezone presence, which is
        // an equality key, so a zero CompareTo would let any comparison-based collection drop one of them.
        FhirTemporal.TryParse("2012-01-01T14:30:00Z", FhirPrimitive.Instant, out var withTimezone);
        FhirTemporal.TryParse("2012-01-01T14:30:00", FhirPrimitive.DateTime, out var withoutTimezone);

        // Assert
        withTimezone.ShouldNotBeNull();
        withoutTimezone.ShouldNotBeNull();
        withTimezone.Equals(withoutTimezone).ShouldBeFalse();
        withTimezone.CompareTo(withoutTimezone).ShouldNotBe(0);
        withoutTimezone.CompareTo(withTimezone).ShouldNotBe(0);
        Math.Sign(withTimezone.CompareTo(withoutTimezone))
            .ShouldBe(-Math.Sign(withoutTimezone.CompareTo(withTimezone)));
    }

    [Fact]
    public void GivenTimezoneBearingAndTimezoneLessSameClock_WhenPutInASortedSet_ThenBothSurvive()
    {
        // Arrange
        FhirTemporal.TryParse("2012-01-01T14:30:00Z", FhirPrimitive.Instant, out var withTimezone);
        FhirTemporal.TryParse("2012-01-01T14:30:00", FhirPrimitive.DateTime, out var withoutTimezone);

        // Act
        SortedSet<FhirTemporal> sorted = [withTimezone!, withoutTimezone!];
        var distinct = new[] { withTimezone!, withoutTimezone! }.Distinct().ToList();

        // Assert
        sorted.Count.ShouldBe(2);
        distinct.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("2012-01-01T14:30:00Z", FhirPrimitive.Instant, "2012-01-01T14:30:00", FhirPrimitive.DateTime)]
    [InlineData("2012-01-01T14:30:00Z", FhirPrimitive.Instant, "2012-01-01T14:30:00Z", FhirPrimitive.DateTime)]
    [InlineData("2012-01-01T10:00:00Z", FhirPrimitive.Instant, "2012-01-01T20:00:00+10:00", FhirPrimitive.Instant)]
    [InlineData("2012-01-01", FhirPrimitive.Date, "2012-01-01T00:00:00", FhirPrimitive.DateTime)]
    [InlineData("2012", FhirPrimitive.Date, "2012-01", FhirPrimitive.Date)]
    [InlineData("13:45:00", FhirPrimitive.Time, "1900-01-01T13:45:00", FhirPrimitive.DateTime)]
    [InlineData("2012-01-01T14:30:00", FhirPrimitive.DateTime, "2012-01-01T14:30:00.5", FhirPrimitive.DateTime)]
    public void GivenTwoValues_WhenComparedWithCompareTo_ThenZeroHoldsExactlyWhenTheyAreEqual(
        string leftLiteral,
        FhirPrimitive leftKind,
        string rightLiteral,
        FhirPrimitive rightKind)
    {
        // Arrange
        FhirTemporal.TryParse(leftLiteral, leftKind, out var left);
        FhirTemporal.TryParse(rightLiteral, rightKind, out var right);

        // Assert
        left.ShouldNotBeNull();
        right.ShouldNotBeNull();
        (left.CompareTo(right) == 0).ShouldBe(left.Equals(right));
        (right.CompareTo(left) == 0).ShouldBe(right.Equals(left));
    }

    [Fact]
    public void GivenAKindThatDisagreesWithTheLiteral_WhenParsed_ThenHasTimezoneStillFollowsTheLiteral()
    {
        // Kind is unvalidated schema metadata, so a dateTime-shaped literal can arrive labelled as a date.
        // The ordering keys are derived from the literal, so HasTimezone must be too -- otherwise the
        // instance reports a floating local time while ordering as a fixed instant, and because
        // HasTimezone is an equality and ordering key that inconsistency propagates into collections.
        FhirTemporal.TryParse("2012-01-01T14:30:00Z", FhirPrimitive.Date, out var mislabelled);
        FhirTemporal.TryParse("2012-01-01T14:30:00Z", FhirPrimitive.DateTime, out var correctlyLabelled);

        // Assert
        mislabelled.ShouldNotBeNull();
        correctlyLabelled.ShouldNotBeNull();
        mislabelled.HasTimezone.ShouldBeTrue();
        mislabelled.Equals(correctlyLabelled).ShouldBeTrue();
        mislabelled.CompareTo(correctlyLabelled).ShouldBe(0);
    }

    [Fact]
    public void GivenTheSameTimeWrittenWithAndWithoutTheLeadingT_WhenParsed_ThenHasTimezoneAgrees()
    {
        // Normalize() supplies the leading 'T' before the timezone scan, so the two spellings cannot
        // disagree on an equality key.
        FhirTemporal.TryParse("13:45:00", FhirPrimitive.Time, out var bare);
        FhirTemporal.TryParse("T13:45:00", FhirPrimitive.Time, out var prefixed);

        // Assert
        bare.ShouldNotBeNull();
        prefixed.ShouldNotBeNull();
        bare.HasTimezone.ShouldBe(prefixed.HasTimezone);
        bare.HasTimezone.ShouldBeFalse();
        bare.CompareTo(prefixed).ShouldBe(0);
        bare.Equals(prefixed).ShouldBeTrue();
    }
}
