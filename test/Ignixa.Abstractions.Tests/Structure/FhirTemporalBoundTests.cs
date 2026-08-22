// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Globalization;

namespace Ignixa.Abstractions.Tests;

/// <summary>
/// Pins <see cref="FhirTemporal.GetLowerBound"/> and <see cref="FhirTemporal.GetUpperBound"/>, the
/// interval arithmetic that FHIRPath ordering and partial-precision arithmetic now share.
/// </summary>
/// <remarks>
/// <para>
/// These are <b>guards</b>, not regression tests. Both helpers already behaved this way; the change
/// that motivated them only widened their accessibility from <c>private</c> to <c>internal</c> so
/// that <c>FhirPathEvaluator</c> could delete its own divergent copy of the same arithmetic. A
/// differential probe of both revisions returns identical values for every row below, so nothing
/// here can fail against the pre-delegation commit - it cannot even compile there, because the
/// helpers were private.
/// </para>
/// <para>
/// They earn their place by pinning the behaviour the deleted copy got wrong, so that the two can
/// never diverge again: saturation instead of overflow at the top of the range, an explicit year
/// range check instead of a throw absorbed by a bare <c>catch</c>, and invariant-culture parsing.
/// The evaluator-level consequences are covered by
/// <c>Ignixa.FhirPath.Tests.Evaluation.FhirPathDateTimeBoundDelegationTests</c>.
/// </para>
/// </remarks>
public class FhirTemporalBoundTests
{
    /// <summary>
    /// Every precision the helpers resolve, with the exact interval each literal denotes. Hour is
    /// absent because it resolves to nothing at all; <see cref="GivenAnHourPrecisionLiteral_WhenBoundsAreComputed_ThenBothAreAbsent"/>
    /// covers that tier.
    /// </summary>
    [Theory]
    [InlineData("2020", FhirTemporalPrecision.Year, "2020-01-01T00:00:00.000", "2020-12-31T23:59:59.999")]
    [InlineData("2020-06", FhirTemporalPrecision.Month, "2020-06-01T00:00:00.000", "2020-06-30T23:59:59.999")]
    [InlineData("2020-02", FhirTemporalPrecision.Month, "2020-02-01T00:00:00.000", "2020-02-29T23:59:59.999")]
    [InlineData("2020-06-15", FhirTemporalPrecision.Day, "2020-06-15T00:00:00.000", "2020-06-15T23:59:59.999")]
    [InlineData("2020-06-15T10:30", FhirTemporalPrecision.Minute, "2020-06-15T10:30:00.000", "2020-06-15T10:30:59.999")]
    [InlineData("2020-06-15T10:30:45", FhirTemporalPrecision.Second, "2020-06-15T10:30:45.000", "2020-06-15T10:30:45.999")]
    [InlineData("2020-06-15T10:30:45Z", FhirTemporalPrecision.Second, "2020-06-15T10:30:45.000", "2020-06-15T10:30:45.999")]

    // Millisecond is exact rather than an interval, so both bounds land on the same instant. The
    // deleted copy spelled this as a dedicated `Millisecond => dt` case ahead of its `_ => dt`
    // fallback; the two arms were always the same value, and this row is what keeps them so.
    [InlineData("2020-06-15T10:30:45.123", FhirTemporalPrecision.Millisecond, "2020-06-15T10:30:45.123", "2020-06-15T10:30:45.123")]
    [InlineData("2020-06-15T10:30:45.123+10:00", FhirTemporalPrecision.Millisecond, "2020-06-15T00:30:45.123", "2020-06-15T00:30:45.123")]
    public void GivenALiteralAtAGivenPrecision_WhenBoundsAreComputed_ThenTheyCoverExactlyThatInterval(
        string literal,
        FhirTemporalPrecision precision,
        string expectedLower,
        string expectedUpper)
    {
        // Act
        var lower = FhirTemporal.GetLowerBound(literal, precision);
        var upper = FhirTemporal.GetUpperBound(literal, precision);

        // Assert
        lower.ShouldBe(Utc(expectedLower));
        upper.ShouldBe(Utc(expectedUpper));

        // The bounds are compared against each other across operands, so a local-time bound would
        // silently shift an ordering by the host's offset.
        lower!.Value.Kind.ShouldBe(DateTimeKind.Utc);
        upper!.Value.Kind.ShouldBe(DateTimeKind.Utc);
    }

    /// <summary>
    /// The hour tier is inert: no supported parse accepts an hour-precision literal, so both bounds
    /// are absent and the comparison that consumes them is indeterminate.
    /// </summary>
    [Fact]
    public void GivenAnHourPrecisionLiteral_WhenBoundsAreComputed_ThenBothAreAbsent()
    {
        // Act
        var lower = FhirTemporal.GetLowerBound("2020-06-15T10", FhirTemporalPrecision.Hour);
        var upper = FhirTemporal.GetUpperBound("2020-06-15T10", FhirTemporalPrecision.Hour);

        // Assert
        lower.ShouldBeNull();
        upper.ShouldBeNull();
    }

    /// <summary>
    /// The substance of the delegation. Computing an upper bound by stepping one unit past the
    /// literal and coming back a millisecond overflows for any value at the very top of
    /// <see cref="DateTime"/>'s range, and <c>9999-12-31</c> is both a valid FHIR date and the
    /// conventional open-ended-<c>Period</c> sentinel. Each of these five returned no bound at all
    /// under the deleted implementation.
    /// </summary>
    [Theory]
    [InlineData("9999-12", FhirTemporalPrecision.Month)]
    [InlineData("9999-12-31", FhirTemporalPrecision.Day)]
    [InlineData("9999-12-31T23:59", FhirTemporalPrecision.Minute)]
    [InlineData("9999-12-31T23:59:59", FhirTemporalPrecision.Second)]
    [InlineData("9999-12-31T23:59:59Z", FhirTemporalPrecision.Second)]
    public void GivenALiteralAtTheTopOfTheRange_WhenTheUpperBoundIsComputed_ThenItSaturatesRatherThanOverflowing(
        string literal,
        FhirTemporalPrecision precision)
    {
        // Act
        var upper = FhirTemporal.GetUpperBound(literal, precision);

        // Assert
        upper.ShouldBe(Utc("9999-12-31T23:59:59.999"));
    }

    /// <summary>
    /// Saturation must not collapse the interval onto its own start, or the boundary values would
    /// order as instants and stop containing anything.
    /// </summary>
    [Fact]
    public void GivenTheLastMonthOfTheRange_WhenBoundsAreComputed_ThenTheIntervalStillSpansTheMonth()
    {
        // Act
        var lower = FhirTemporal.GetLowerBound("9999-12", FhirTemporalPrecision.Month);
        var upper = FhirTemporal.GetUpperBound("9999-12", FhirTemporalPrecision.Month);

        // Assert
        lower.ShouldBe(Utc("9999-12-01T00:00:00.000"));
        upper.ShouldBe(Utc("9999-12-31T23:59:59.999"));
    }

    /// <summary>
    /// A year outside <see cref="DateTime"/>'s range is rejected by an explicit range check. The
    /// deleted copy reached the same answer by letting the <see cref="DateTime"/> constructor throw
    /// into a bare <c>catch</c>, so this is hygiene rather than behaviour - the observable outcome
    /// was already "no bound", and only the cost of the throw differed.
    /// </summary>
    [Theory]
    [InlineData("12345")]
    [InlineData("0")]
    [InlineData("10000")]
    public void GivenAYearOutsideTheRepresentableRange_WhenBoundsAreComputed_ThenTheyAreAbsentWithoutThrowing(
        string literal)
    {
        // Act
        DateTime? lower = null;
        DateTime? upper = null;
        var compute = () =>
        {
            lower = FhirTemporal.GetLowerBound(literal, FhirTemporalPrecision.Year);
            upper = FhirTemporal.GetUpperBound(literal, FhirTemporalPrecision.Year);
        };

        // Assert
        compute.ShouldNotThrow();
        lower.ShouldBeNull();
        upper.ShouldBeNull();
    }

    /// <summary>
    /// The one deliberate narrowing the delegation carries. The deleted copy parsed the year with
    /// <see cref="NumberStyles.Integer"/>, which tolerates a leading sign and surrounding
    /// whitespace, so <c>"+2020"</c> resolved to the year 2020. FHIR's <c>date</c> and
    /// <c>dateTime</c> regexes permit neither, so these are not FHIR values and FHIRPath prescribes
    /// an empty result for an invalid operand.
    /// </summary>
    [Theory]
    [InlineData("+2020")]
    [InlineData("-2020")]
    [InlineData(" 2020")]
    [InlineData("2020 ")]
    public void GivenAYearCarryingASignOrSurroundingWhitespace_WhenBoundsAreComputed_ThenItIsRejected(
        string literal)
    {
        // Act
        var lower = FhirTemporal.GetLowerBound(literal, FhirTemporalPrecision.Year);
        var upper = FhirTemporal.GetUpperBound(literal, FhirTemporalPrecision.Year);

        // Assert
        lower.ShouldBeNull();
        upper.ShouldBeNull();
    }

    /// <summary>
    /// Guard on the parse's explicit <see cref="CultureInfo.InvariantCulture"/> arguments, which
    /// have no other coverage.
    /// </summary>
    /// <remarks>
    /// The month tier carries this test: it parses through <see cref="DateTime.TryParseExact(string, string, IFormatProvider, DateTimeStyles, out DateTime)"/>,
    /// and a culture-bound parse would read <c>9999-12</c> in the ambient calendar - under
    /// <c>th-TH</c> the Buddhist era yields year 9456, and under <c>ar-SA</c> the Umm al-Qura
    /// calendar rejects the date outright. The year tier is along for the ride and proves little on
    /// its own, because a bare ASCII integer parses identically under every culture .NET ships.
    /// </remarks>
    [Theory]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    public void GivenAHostileAmbientCulture_WhenBoundsAreComputed_ThenTheyAreUnchanged(string culture)
    {
        // Arrange
        var previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);

        try
        {
            // Act
            var yearLower = FhirTemporal.GetLowerBound("9999", FhirTemporalPrecision.Year);
            var monthLower = FhirTemporal.GetLowerBound("9999-12", FhirTemporalPrecision.Month);
            var monthUpper = FhirTemporal.GetUpperBound("9999-12", FhirTemporalPrecision.Month);

            // Assert
            yearLower.ShouldBe(Utc("9999-01-01T00:00:00.000"));
            monthLower.ShouldBe(Utc("9999-12-01T00:00:00.000"));
            monthUpper.ShouldBe(Utc("9999-12-31T23:59:59.999"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private static DateTime Utc(string value) => DateTime.SpecifyKind(
        DateTime.ParseExact(value, "yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture),
        DateTimeKind.Utc);
}
