// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

namespace Ignixa.Abstractions.Tests;

/// <summary>
/// Tests for <see cref="FhirQuantity"/>, pinning the contract this assembly publishes: a value
/// carrier with value semantics and no opinion about unit algebra.
/// </summary>
public class FhirQuantityTests
{
    [Fact]
    public void GivenValueAndUnit_WhenConstructed_ThenBothAreExposedUnchanged()
    {
        // Arrange & Act
        var quantity = new FhirQuantity(4.5m, "mg");

        // Assert
        quantity.Value.ShouldBe(4.5m);
        quantity.Unit.ShouldBe("mg");
    }

    [Fact]
    public void GivenNullUnit_WhenConstructed_ThenThrows()
    {
        // A quantity without a unit is not a degraded quantity, it is a decimal. Callers meaning
        // "dimensionless" pass UCUM's "1", which is what the evaluator's implicit numeric
        // conversion does.
        Should.Throw<ArgumentNullException>(() => new FhirQuantity(1m, null!));
    }

    [Fact]
    public void GivenSameValueAndUnit_WhenCompared_ThenEqual()
    {
        // Arrange
        var left = new FhirQuantity(4.5m, "mg");
        var right = new FhirQuantity(4.5m, "mg");

        // Assert
        left.Equals(right).ShouldBeTrue();
        (left == right).ShouldBeTrue();
        left.GetHashCode().ShouldBe(right.GetHashCode());
    }

    [Fact]
    public void GivenConvertibleUnits_WhenCompared_ThenNotEqualBecauseNoConversionHappensHere()
    {
        // 7 'd' and 1 'wk' are the same duration, but deciding that needs UCUM. This assembly has no
        // unit converter and must not grow one: callers wanting conversion go through
        // IQuantityUnitConverter in Ignixa.FhirPath first.
        var days = new FhirQuantity(7m, "d");
        var week = new FhirQuantity(1m, "wk");

        days.Equals(week).ShouldBeFalse();
    }

    [Fact]
    public void GivenCalendarKeywordAndItsUcumCode_WhenCompared_ThenNotEqual()
    {
        // A calendar keyword is calendar-aware and its UCUM code is a fixed duration, so folding the
        // two together here would make the type lie about a distinction FHIRPath depends on.
        var keyword = new FhirQuantity(1m, "week");
        var ucum = new FhirQuantity(1m, "wk");

        keyword.Equals(ucum).ShouldBeFalse();
    }

    [Fact]
    public void GivenAnotherType_WhenCompared_ThenNotEqual()
    {
        new FhirQuantity(1m, "mg").Equals("1 'mg'").ShouldBeFalse();
    }

    [Fact]
    public void GivenNull_WhenComparedThroughOperators_ThenHandledWithoutThrowing()
    {
        FhirQuantity? none = null;

        (none == null).ShouldBeTrue();
        (new FhirQuantity(1m, "mg") == none).ShouldBeFalse();
        (none != new FhirQuantity(1m, "mg")).ShouldBeTrue();
    }

    [Theory]
    [InlineData("1", "mg", "1 'mg'")]
    [InlineData("1.50", "mg", "1.50 'mg'")]
    [InlineData("1.5", "mg", "1.5 'mg'")]
    [InlineData("1", "week", "1 week")]
    [InlineData("1", "milliseconds", "1 milliseconds")]
    [InlineData("1", "wk", "1 'wk'")]
    [InlineData("3.14159", "rad", "3.14159 'rad'")]
    public void GivenAQuantity_WhenStringified_ThenEmitsFhirPathLiteralAtItsStatedPrecision(
        string value,
        string unit,
        string expected)
    {
        // The decimal's scale is the quantity's precision, which is why there is no separate
        // Precision member: 1.50 'mg' has to survive a round trip as written.
        var quantity = new FhirQuantity(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), unit);

        quantity.ToString().ShouldBe(expected);
    }

    [Fact]
    public void GivenValuesDifferingOnlyInScale_WhenCompared_ThenEqualButRenderedDistinctly()
    {
        // Trailing zeros are precision, not identity: FHIRPath defines = on quantities by value and
        // unit and leaves precision to ~, so equality must not read the scale that ToString does.
        var stated = new FhirQuantity(1.50m, "mg");
        var terse = new FhirQuantity(1.5m, "mg");

        stated.Equals(terse).ShouldBeTrue();
        stated.ToString().ShouldNotBe(terse.ToString());
        stated.GetHashCode().ShouldBe(terse.GetHashCode());
    }
}
