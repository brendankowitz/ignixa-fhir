// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

namespace Ignixa.Abstractions.Tests;

/// <summary>
/// Covers <see cref="FhirTemporal.TryParse"/>'s "This never throws" contract at the top of
/// <see cref="DateTime"/>'s range.
/// </summary>
/// <remarks>
/// <para>
/// The upper bound of a partial-precision value was computed by stepping one unit past the literal and
/// coming back a millisecond -- <c>AddDays(1).AddMilliseconds(-1)</c> and its siblings. The intermediate
/// is out of range for a literal at the end of year 9999, so <c>TryParse</c> threw
/// <see cref="ArgumentOutOfRangeException"/> instead of returning. Every one of these dates is a valid
/// FHIR value, and <c>9999-12-31</c> in particular is a common open-ended-period sentinel.
/// </para>
/// <para>
/// The contract matters because callers treat <see langword="false"/> as "keep the raw string" -- an
/// expected outcome for untrusted wire data -- and have no handler for an exception. It was newly
/// reachable rather than newly broken: <c>ValueOrdering.CompareTemporals</c> calls the parser on an
/// uncaught path, and <c>SortComparer</c> deliberately dropped the bare <c>catch</c> that used to
/// swallow this as "equal".
/// </para>
/// </remarks>
public class FhirTemporalRangeBoundaryTests
{
    [Theory]
    [InlineData("9999", FhirPrimitive.Date)]
    [InlineData("9999-12", FhirPrimitive.Date)]
    [InlineData("9999-12-31", FhirPrimitive.Date)]
    [InlineData("9999-12-31T23:59:59Z", FhirPrimitive.Instant)]
    [InlineData("9999-12-31T23:59:59", FhirPrimitive.DateTime)]
    [InlineData("9999-12-31T23:59", FhirPrimitive.DateTime)]
    [InlineData("9999-12-31T23", FhirPrimitive.DateTime)]
    public void GivenALiteralAtTheTopOfTheRange_WhenParsed_ThenItReturnsRatherThanThrowing(
        string literal,
        FhirPrimitive kind)
    {
        // Act
        var parse = () => FhirTemporal.TryParse(literal, kind, out _);

        // Assert
        parse.ShouldNotThrow();
    }

    [Theory]
    [InlineData("9999-12-31", FhirPrimitive.Date)]
    [InlineData("9999-12", FhirPrimitive.Date)]
    [InlineData("9999", FhirPrimitive.Date)]
    public void GivenALiteralAtTheTopOfTheRange_WhenParsed_ThenItIsAcceptedAndRoundTrips(
        string literal,
        FhirPrimitive kind)
    {
        // Act
        var parsed = FhirTemporal.TryParse(literal, kind, out var result);

        // Assert
        parsed.ShouldBeTrue();
        result!.Literal.ShouldBe(literal);
    }

    /// <summary>
    /// The saturated upper bound has to leave ordering intact, or refusing to throw would have bought a
    /// wrong answer instead.
    /// </summary>
    [Fact]
    public void GivenTheLastDayOfTheRangeAndAnEarlierOne_WhenCompared_ThenTheyStillOrder()
    {
        // Arrange
        FhirTemporal.TryParse("9999-12-31", FhirPrimitive.Date, out var last);
        FhirTemporal.TryParse("9999-12-30", FhirPrimitive.Date, out var earlier);

        // Act
        var order = FhirTemporal.Compare(earlier, last);

        // Assert
        order.ShouldNotBeNull();
        order.Value.ShouldBeLessThan(0);
    }

    /// <summary>
    /// A saturated December still has to contain the day inside it rather than collapsing onto it, or the
    /// partial-precision overlap rule would break at the boundary.
    /// </summary>
    [Fact]
    public void GivenTheLastMonthOfTheRangeAndADayInsideIt_WhenCompared_ThenTheOrderingIsIndeterminate()
    {
        // Arrange
        FhirTemporal.TryParse("9999-12", FhirPrimitive.Date, out var month);
        FhirTemporal.TryParse("9999-12-15", FhirPrimitive.Date, out var day);

        // Act
        var order = FhirTemporal.Compare(month, day);

        // Assert
        order.ShouldBeNull();
    }

    /// <summary>
    /// Guard: the saturation must not have widened an ordinary interval. A month that is nowhere near the
    /// boundary still ends where it always did.
    /// </summary>
    [Fact]
    public void GivenAnOrdinaryMonthAndTheFollowingMonth_WhenCompared_ThenTheyOrderDeterminately()
    {
        // Arrange
        FhirTemporal.TryParse("2012-01", FhirPrimitive.Date, out var january);
        FhirTemporal.TryParse("2012-02", FhirPrimitive.Date, out var february);

        // Act
        var order = FhirTemporal.Compare(january, february);

        // Assert
        order.ShouldNotBeNull();
        order.Value.ShouldBeLessThan(0);
    }
}
