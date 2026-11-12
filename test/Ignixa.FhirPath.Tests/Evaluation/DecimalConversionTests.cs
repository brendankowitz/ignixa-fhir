using System;
using System.Collections.Generic;
using System.Globalization;
using Fhir.Metrics;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Types;
using Shouldly;
using Xunit;

// Seeded System.Random only generates reproducible test inputs; nothing here is security sensitive.
#pragma warning disable CA5394

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// .NET 11 made (decimal)double exact. <see cref="DecimalConversion"/> keeps the pre-.NET 11 results on every
/// runtime; the fixed vectors run on all target frameworks and the differential tests compare against the real
/// cast wherever it still has the old behaviour.
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

    // 1/3 has 28 digits. Where the runtime cast is correctly rounded, Fhir.Metrics' own significands are noisy and are
    // rounded to 15 significant digits (the accepted .NET 11 precision outcome); before .NET 11 nothing is rounded.
#if NET11_0_OR_GREATER
    private const decimal OneThird = 0.333333333333333m;
#else
    private const decimal OneThird = 0.3333333333333333333333333333m;
#endif

    [Fact]
    public void GivenLongQuotient_WhenConvertedToDecimal_ThenPrecisionMatchesRuntimeBehaviour()
    {
        var third = (Exponential)1m / (Exponential)3m;

        DecimalConversion.ToDecimal(third).ShouldBe(OneThird);
    }

    [Fact]
    public void GivenQuantitiesWithLongQuotient_WhenDivided_ThenUcumResultMatchesRuntimeBehaviour()
    {
        var result = QuantityUnitConverter.Instance.Divide(new FhirQuantity(1m, "m"), new FhirQuantity(3m, "m"));

        result.ShouldNotBeNull();
        result.Value.ShouldBe(OneThird);
    }

    [Theory]
    [InlineData(0.0d, "0")]
    [InlineData(0.01d, "0.01")]
    [InlineData(-0.01d, "-0.01")]
    [InlineData(100d, "100")]
    [InlineData(1e15d, "1000000000000000")]
    [InlineData(1e28d, "10000000000000000000000000000")]
    [InlineData(1e-28d, "0.0000000000000000000000000001")]
    [InlineData(1e-29d, "0")]
    [InlineData(-1e-29d, "0")]
    [InlineData(5e-324d, "0")]
    [InlineData(0.1d + 0.2d, "0.3")]
    [InlineData(1d / 3d, "0.333333333333333")]
    [InlineData(123456789.123456789d, "123456789.123457")]
    [InlineData(2.5e-5d, "0.000025")]
    public void GivenDouble_WhenNarrowed_ThenResultMatchesPreNet11Cast(double input, string expected)
    {
        var result = DecimalConversion.FromDouble(input);

        BitsOf(result).ShouldBe(BitsOf(decimal.Parse(expected, CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData(0f, "0")]
    [InlineData(0.1f, "0.1")]
    [InlineData(1f / 3f, "0.3333333")]
    [InlineData(16777216f, "16777220")]
    [InlineData(1e-28f, "0.0000000000000000000000000001")]
    [InlineData(1e-30f, "0")]
    public void GivenSingle_WhenNarrowed_ThenResultMatchesPreNet11Cast(float input, string expected)
    {
        var result = DecimalConversion.FromSingle(input);

        BitsOf(result).ShouldBe(BitsOf(decimal.Parse(expected, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void GivenNegativeZero_WhenNarrowed_ThenResultIsPositiveZeroWithScaleZero()
    {
        BitsOf(DecimalConversion.FromDouble(-0.0d)).ShouldBe(BitsOf(0m));
        BitsOf(DecimalConversion.FromSingle(-0f)).ShouldBe(BitsOf(0m));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(7.9228162514264338e28d)] // 2^96: the first magnitude above decimal.MaxValue
    [InlineData(-7.9228162514264338e28d)]
    [InlineData(1e29d)]
    [InlineData(double.MaxValue)]
    public void GivenDoubleOutsideDecimalRange_WhenNarrowed_ThenOverflowIsThrown(double input)
    {
        Should.Throw<OverflowException>(() => DecimalConversion.FromDouble(input));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(7.9228163e28f)] // 2^96
    [InlineData(float.MaxValue)]
    public void GivenSingleOutsideDecimalRange_WhenNarrowed_ThenOverflowIsThrown(float input)
    {
        Should.Throw<OverflowException>(() => DecimalConversion.FromSingle(input));
    }

    [Theory]
    [InlineData(-1, "0.1")]
    [InlineData(-28, "0.0000000000000000000000000001")]
    [InlineData(-29, "0")]
    [InlineData(-400, "0")]
    [InlineData(0, "1")]
    [InlineData(28, "10000000000000000000000000000")]
    public void GivenExponentialWithPowerOfTen_WhenConvertedToDecimal_ThenScaleMatchesPreNet11Behaviour(int exponent, string powerOfTen)
    {
        var expected = 1.5m * decimal.Parse(powerOfTen, CultureInfo.InvariantCulture);

        BitsOf(DecimalConversion.ToDecimal(new Exponential(1.5m, exponent))).ShouldBe(BitsOf(expected));
    }

    [Theory]
    [InlineData(29)]
    [InlineData(400)]
    public void GivenExponentialTooLargeForDecimal_WhenConvertedToDecimal_ThenOverflowIsThrown(int exponent)
    {
        Should.Throw<OverflowException>(() => DecimalConversion.ToDecimal(new Exponential(1m, exponent)));
    }

#if !NET11_0_OR_GREATER
    // On runtimes before .NET 11 the built-in cast is the specification, so the port is compared with it bit for bit
    // (value, scale and sign) and the overflow outcome must agree too.

    [Fact]
    public void GivenRandomDoubles_WhenNarrowed_ThenResultEqualsBuiltInCast()
    {
        foreach (var value in DoubleSamples())
        {
            Outcome(() => DecimalConversion.FromDouble(value)).ShouldBe(Outcome(() => (decimal)value), $"double {value:R}");
        }
    }

    [Fact]
    public void GivenRandomSingles_WhenNarrowed_ThenResultEqualsBuiltInCast()
    {
        foreach (var value in SingleSamples())
        {
            Outcome(() => DecimalConversion.FromSingle(value)).ShouldBe(Outcome(() => (decimal)value), $"float {value:R}");
        }
    }

    [Fact]
    public void GivenEdgeCases_WhenNarrowed_ThenResultEqualsBuiltInCast()
    {
        var maxAsDouble = (double)decimal.MaxValue;
        var doubles = new List<double>
        {
            0d, -0d, double.Epsilon, -double.Epsilon, double.MaxValue, double.MinValue, double.NaN,
            double.PositiveInfinity, double.NegativeInfinity, 1e-28, 1e-29, 5e-29, 4.9e-29, 1e-30, 1e28, 1e29,
            maxAsDouble, -maxAsDouble, Math.BitDecrement(maxAsDouble), Math.BitIncrement(maxAsDouble),
            Math.BitDecrement(Math.Pow(2, 96)), Math.Pow(2, 96), Math.Pow(2, -94), Math.Pow(2, -95), 0.5, 1.5, 0.1, 0.3,
            0.1 + 0.2, 1d / 3d, 2d / 3d, 99999999999999.5, 999999999999999.5, 1e15, 1e14, 123456789012345678d,
        };

        var floats = new List<float>
        {
            0f, -0f, float.Epsilon, -float.Epsilon, float.MaxValue, float.MinValue, float.NaN, float.PositiveInfinity,
            float.NegativeInfinity, 1e-28f, 1e-29f, 1e-30f, 1e28f, 1e29f, (float)decimal.MaxValue,
            MathF.BitDecrement((float)decimal.MaxValue), MathF.BitIncrement((float)decimal.MaxValue),
            MathF.BitDecrement(MathF.Pow(2, 96)), MathF.Pow(2, 96), MathF.Pow(2, -94), MathF.Pow(2, -95), 0.1f, 0.3f,
            1f / 3f, 16777216f, 9999999.5f, 1e7f, 1e6f,
        };

        foreach (var value in doubles)
        {
            Outcome(() => DecimalConversion.FromDouble(value)).ShouldBe(Outcome(() => (decimal)value), $"double {value:R}");
        }

        foreach (var value in floats)
        {
            Outcome(() => DecimalConversion.FromSingle(value)).ShouldBe(Outcome(() => (decimal)value), $"float {value:R}");
        }
    }

    [Fact]
    public void GivenAnyExponent_WhenPowerOfTenRequested_ThenScaleMatchesBuiltInCast()
    {
        for (var n = -40; n <= 40; n++)
        {
            Outcome(() => DecimalConversion.PowerOfTen(n)).ShouldBe(Outcome(() => (decimal)Math.Pow(10.0, n)), $"10^{n}");
        }
    }

    [Fact]
    public void GivenAnyExponential_WhenConvertedToDecimal_ThenResultEqualsLibraryConversion()
    {
        var random = new Random(20260511);
        var significands = new List<decimal>
        {
            0m, 1m, -1m, 1.5m, 0.01m, 1.0000m, 79228162514264337593543950335m, 0.3333333333333333333333333333m,
            -0.6666666666666666666666666667m, 1234567.890123456789m,
        };

        for (var i = 0; i < 40; i++)
        {
            significands.Add(new decimal(random.Next(), random.Next(), random.Next(0, 1 << 20), random.Next(2) == 0, (byte)random.Next(0, 29)));
        }

        foreach (var significand in significands)
        {
            for (var exponent = -40; exponent <= 40; exponent++)
            {
                var value = new Exponential(significand, exponent);

                Outcome(() => DecimalConversion.ToDecimal(value)).ShouldBe(Outcome(() => value.ToDecimal()), $"{significand} * 10^{exponent}");
            }
        }
    }

    private static IEnumerable<double> DoubleSamples()
    {
        var random = new Random(20260510);

        // Uniform bit patterns reach every exponent, including subnormals and non-finite values.
        for (var i = 0; i < 200_000; i++)
        {
            yield return BitConverter.Int64BitsToDouble(random.NextInt64());
        }

        // Values a decimal can actually hold, per decade, with and without short decimal expansions.
        for (var decade = -32; decade <= 32; decade++)
        {
            for (var i = 0; i < 2_000; i++)
            {
                var value = random.NextDouble() * Math.Pow(10, decade);
                yield return value;
                yield return -value;
                yield return Math.Round(value, random.Next(0, 16));
                yield return Math.Floor(value) + 0.5;
            }
        }

        // Integers around the 15 digit boundary, also scaled by powers of ten.
        for (var i = 0; i < 20_000; i++)
        {
            var integer = (double)random.NextInt64(100_000_000_000_000L, 1_000_000_000_000_001L);
            yield return integer;
            yield return integer + 0.5;
            yield return integer * Math.Pow(10, random.Next(-30, 14));
        }
    }

    private static IEnumerable<float> SingleSamples()
    {
        var random = new Random(20260512);

        for (var i = 0; i < 200_000; i++)
        {
            yield return BitConverter.Int32BitsToSingle(random.Next(int.MinValue, int.MaxValue));
        }

        for (var decade = -32; decade <= 32; decade++)
        {
            for (var i = 0; i < 2_000; i++)
            {
                var value = (float)(random.NextDouble() * Math.Pow(10, decade));
                yield return value;
                yield return -value;
                yield return MathF.Round(value, random.Next(0, 7));
                yield return MathF.Floor(value) + 0.5f;
            }
        }

        for (var i = 0; i < 20_000; i++)
        {
            var integer = (float)random.Next(1_000_000, 10_000_001);
            yield return integer;
            yield return integer + 0.5f;
            yield return integer * (float)Math.Pow(10, random.Next(-30, 22));
        }
    }

    private static string Outcome(Func<decimal> convert)
    {
        try
        {
            return BitsOf(convert());
        }
        catch (OverflowException)
        {
            return "overflow";
        }
    }
#endif

    private static string BitsOf(decimal value) => string.Join(",", decimal.GetBits(value));
}
