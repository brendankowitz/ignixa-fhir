using System;
using System.Globalization;
using Fhir.Metrics;
using Ignixa.FhirPath.Types;
using Shouldly;
using Xunit;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// .NET 11 made (decimal)double exact, so these pin the runtime-independent behaviour the evaluator relies on.
/// </summary>
public class DecimalConversionTests
{
    [Theory]
    [InlineData(12.34d, "12.34")]
    [InlineData(1.23d, "1.23")]
    [InlineData(0.1d, "0.1")]
    [InlineData(-2.5d, "-2.5")]
    public void GivenDouble_WhenConvertedToDecimal_ThenBinaryNoiseIsDropped(double input, string expected)
    {
        var result = DecimalConversion.FromDouble(input);

        result.ShouldBe(decimal.Parse(expected, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void GivenSingle_WhenConvertedToDecimal_ThenBinaryNoiseIsDropped()
    {
        DecimalConversion.FromSingle(1.1f).ShouldBe(1.1m);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void GivenNonFiniteDouble_WhenConvertedToDecimal_ThenOverflowIsThrown(double input)
    {
        Should.Throw<OverflowException>(() => DecimalConversion.FromDouble(input));
    }

    [Theory]
    [InlineData(-5, "0.00001")]
    [InlineData(-1, "0.1")]
    [InlineData(0, "1")]
    [InlineData(3, "1000")]
    public void GivenExponent_WhenPowerOfTenRequested_ThenValueIsExact(int exponent, string expected)
    {
        var result = DecimalConversion.PowerOfTen(exponent);

        result.ShouldBe(decimal.Parse(expected, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void GivenExponential_WhenConvertedToDecimal_ThenValueIsExact()
    {
        DecimalConversion.ToDecimal(new Exponential(0.01m)).ShouldBe(0.01m);
    }
}
