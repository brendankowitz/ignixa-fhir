/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Runtime-independent conversions from binary floating point and Fhir.Metrics values to decimal.
 */

using System;
using Fhir.Metrics;

namespace Ignixa.FhirPath.Types;

/// <summary>
/// Narrows binary floating-point and <see cref="Exponential"/> values to <see cref="decimal"/> with the
/// semantics of the explicit casts before .NET 11, so that FHIRPath results are the same on every supported runtime.
/// </summary>
/// <remarks>
/// <para>
/// Before .NET 11 the explicit <c>(decimal)double</c> and <c>(decimal)float</c> conversions rounded to 15 and
/// 7 significant digits and dropped trailing zeros, so <c>(decimal)Math.Pow(10, -2)</c> was exactly <c>0.01</c>.
/// From .NET 11 they are correctly rounded and keep the exact binary value (<c>0.01000000000000000020816681712</c>),
/// which breaks equality of converted quantities and the precision arithmetic behind
/// <c>lowBoundary()</c> and <c>highBoundary()</c>.
/// </para>
/// <para>
/// <see cref="FromDouble"/> and <see cref="FromSingle"/> are a port of the former runtime algorithm
/// (<c>Decimal.DecCalc.VarDecFromR8</c> / <c>VarDecFromR4</c>) and return the same bits, including sign, scale and
/// overflow behaviour, on all runtimes. Tests compare them with the real cast on .NET 10.
/// </para>
/// <para>
/// <see cref="Exponential"/> in Fhir.Metrics computes <c>Significand * (decimal)Math.Pow(10, Exponent)</c>.
/// <see cref="ToDecimal(Exponential)"/> evaluates the same product with an exact power of ten, which is what the
/// former cast produced. Where the runtime cast is correctly rounded the library's significands carry binary noise,
/// which is rounded to 15 significant digits; elsewhere the significand is never rounded, so long quotients keep
/// all 28 digits.
/// </para>
/// </remarks>
internal static class DecimalConversion
{
    private const int MaxDecimalScale = 28;
    private const int DoubleSignificantDigits = 15;

    // 1.000...0 with 28 zeros: dividing by it drops trailing zeros without changing the value.
    private const decimal TrailingZeroStripper = 1.0000000000000000000000000000m;

    // The largest power of ten the old algorithm scales by is 10^28; both mantissas stay below 10^15.
    private static readonly double[] DoublePowers10 =
    [
        1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9,
        1e10, 1e11, 1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19,
        1e20, 1e21, 1e22, 1e23, 1e24, 1e25, 1e26, 1e27, 1e28,
    ];

    /// <summary>
    /// Narrows <paramref name="value"/> to the 15 significant digits that a <see cref="double"/> reliably holds.
    /// </summary>
    /// <exception cref="OverflowException">The value is not finite or is outside the range of <see cref="decimal"/>.</exception>
    public static decimal FromDouble(double value)
    {
        // Same layout as Decimal.DecCalc.GetExponent(double): the biased exponent, rebased to a 0.5..1 mantissa.
        var exp = (int)((BitConverter.DoubleToUInt64Bits(value) >> 52) & 0x7FF) - 1022;
        return Narrow(value, exp, significantDigits: 15, lowerBound: 1e14, upperBound: 1e15);
    }

    /// <summary>
    /// Narrows <paramref name="value"/> to the 7 significant digits that a <see cref="float"/> reliably holds.
    /// </summary>
    /// <exception cref="OverflowException">The value is not finite or is outside the range of <see cref="decimal"/>.</exception>
    public static decimal FromSingle(float value)
    {
        var exp = (int)(byte)(BitConverter.SingleToUInt32Bits(value) >> 23) - 126;
        return Narrow(value, exp, significantDigits: 7, lowerBound: 1e6, upperBound: 1e7);
    }

    /// <summary>
    /// Returns exactly 10<sup>n</sup>; a power below 10<sup>-28</sup> underflows to zero.
    /// </summary>
    /// <exception cref="OverflowException"><paramref name="n"/> is too large for a <see cref="decimal"/>.</exception>
    public static decimal PowerOfTen(int n)
    {
        if (n < 0)
        {
            // Carries scale -n, like the 15 digit (decimal)Math.Pow(10, n) it replaces, so that multiplying
            // by it keeps the same trailing zeros as before.
            return -n > MaxDecimalScale ? 0m : new decimal(1, 0, 0, false, (byte)-n);
        }

        var result = 1m;
        for (; n > 0; n--)
        {
            result *= 10m;
        }

        return result;
    }

    /// <summary>
    /// Converts a <see cref="Exponential"/> to <see cref="decimal"/>: <c>Significand * 10^Exponent</c> with an exact
    /// power of ten, matching Fhir.Metrics' own formula before .NET 11.
    /// </summary>
    /// <remarks>
    /// Where the runtime cast is correctly rounded (.NET 11+), Fhir.Metrics itself builds significands from
    /// <c>(decimal)double</c> values, so the result carries binary noise (<c>2149.99999999999995836...</c>) that is
    /// rounded off to 15 significant digits. Earlier runtimes yield noise-free significands, so the result is returned
    /// untouched and long quotients such as <c>1/3</c> keep all 28 digits.
    /// </remarks>
    /// <exception cref="OverflowException">The value is outside the range of <see cref="decimal"/>.</exception>
    public static decimal ToDecimal(Exponential value)
    {
        var result = value.Significand * PowerOfTen(value.Exponent);
        return CastIsCorrectlyRounded ? RemoveNoise(result) : result;
    }

    // Probed at run time rather than compile time so that the right behaviour is chosen for the runtime
    // that actually executes the cast inside Fhir.Metrics.
    private static readonly bool CastIsCorrectlyRounded = (decimal)Math.Pow(10.0, -2) != 0.01m;

    /// <summary>
    /// Rounds to 15 significant digits, and only when that changes the value, so that exact results keep their scale.
    /// </summary>
    private static decimal RemoveNoise(decimal value)
    {
        if (value == 0m)
        {
            return value;
        }

        var magnitude = (int)Math.Floor(Math.Log10((double)Math.Abs(value)));
        var decimals = Math.Min(DoubleSignificantDigits - 1 - magnitude, MaxDecimalScale);

        decimal rounded;
        if (decimals >= 0)
        {
            rounded = decimal.Round(value, decimals, MidpointRounding.AwayFromZero);
        }
        else
        {
            var factor = PowerOfTen(-decimals);
            rounded = decimal.Round(value / factor, 0, MidpointRounding.AwayFromZero) * factor;
        }

        return rounded == value ? value : rounded / TrailingZeroStripper;
    }

    /// <summary>
    /// Port of <c>Decimal.DecCalc.VarDecFromR8</c>/<c>VarDecFromR4</c> from .NET 10. <paramref name="exp"/> is the
    /// binary exponent of <paramref name="input"/> with the 0.5..1 mantissa convention.
    /// </summary>
    private static decimal Narrow(double input, int exp, int significantDigits, double lowerBound, double upperBound)
    {
        // The most the old algorithm scales by is 10^28, just over 2^93, so smaller values always round to zero.
        // This also covers 0, -0 and subnormals, which therefore yield +0 with scale 0.
        if (exp < -94)
        {
            return 0m;
        }

        // Covers NaN, infinities and everything from 2^96 up; decimal.MaxValue is just below 2^96.
        if (exp > 96)
        {
            throw new OverflowException("Value was either too large or too small for a Decimal.");
        }

        var isNegative = input < 0;
        var dbl = Math.Abs(input);

        // Estimate the decimal magnitude as exp * log10(2), in 16 bit fixed point (.30103 * 65536 = 19728.3).
        var power = significantDigits - 1 - ((exp * 19728) >> 16);

        if (power >= 0)
        {
            power = Math.Min(power, MaxDecimalScale);
            dbl *= DoublePowers10[power];
        }
        else if (power != -1 || dbl >= upperBound)
        {
            dbl /= DoublePowers10[-power];
        }
        else
        {
            power = 0;
        }

        if (dbl < lowerBound && power < MaxDecimalScale)
        {
            dbl *= 10;
            power++;
        }

        var mantissa = (ulong)(long)Math.Round(dbl);
        if (mantissa == 0)
        {
            return 0m;
        }

        if (power < 0)
        {
            var scale = UInt128.One;
            for (var i = -power; i > 0; i--)
            {
                scale *= 10;
            }

            var product = mantissa * scale;
            if ((product >> 96) != 0)
            {
                throw new OverflowException("Value was either too large or too small for a Decimal.");
            }

            return new decimal((int)(uint)product, (int)(uint)(product >> 32), (int)(uint)(product >> 64), isNegative, 0);
        }

        // Drop trailing zeros, but never below scale 0 and never more than the digits the mantissa can hold.
        for (var removable = Math.Min(power, significantDigits - 1); removable > 0 && mantissa % 10 == 0; removable--)
        {
            mantissa /= 10;
            power--;
        }

        return new decimal((int)(uint)mantissa, (int)(uint)(mantissa >> 32), 0, isNegative, (byte)power);
    }
}
