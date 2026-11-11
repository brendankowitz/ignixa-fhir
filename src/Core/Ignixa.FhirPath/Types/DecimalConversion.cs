/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Runtime-independent conversions from binary floating point and Fhir.Metrics values to decimal.
 */

using System;
using System.Globalization;
using Fhir.Metrics;

namespace Ignixa.FhirPath.Types;

/// <summary>
/// Narrows binary floating-point and <see cref="Exponential"/> values to <see cref="decimal"/> so that
/// FHIRPath results are the same on every supported runtime.
/// </summary>
/// <remarks>
/// <para>
/// Before .NET 11 the explicit <c>(decimal)double</c> and <c>(decimal)float</c> conversions kept 15 and
/// 7 significant digits, so <c>(decimal)Math.Pow(10, -2)</c> was exactly <c>0.01</c>. From .NET 11 they
/// are correctly rounded and keep the exact binary value (<c>0.01000000000000000020816681712</c>),
/// which breaks equality of converted quantities and the precision arithmetic behind
/// <c>lowBoundary()</c> and <c>highBoundary()</c>.
/// </para>
/// <para>
/// <see cref="Exponential"/> in Fhir.Metrics scales with <c>(decimal)Math.Pow(10, n)</c>, so the values
/// it produces carry that noise on .NET 11. <see cref="ToDecimal(Exponential)"/> rebuilds the value with
/// exact decimal arithmetic and removes the residue left by the library's own scaling.
/// </para>
/// </remarks>
internal static class DecimalConversion
{
    private const int DoubleSignificantDigits = 15;
    private const int MaxDecimalScale = 28;

    // 1.000...0 with 28 zeros: dividing by it drops trailing zeros without changing the value.
    private const decimal TrailingZeroStripper = 1.0000000000000000000000000000m;

    /// <summary>
    /// Narrows <paramref name="value"/> to the 15 significant digits that a <see cref="double"/> reliably holds.
    /// </summary>
    /// <exception cref="OverflowException">The value is not finite or is outside the range of <see cref="decimal"/>.</exception>
    public static decimal FromDouble(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new OverflowException("Value was either too large or too small for a Decimal.");
        }

        return decimal.Parse(
            value.ToString("G15", CultureInfo.InvariantCulture),
            NumberStyles.Float,
            CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Narrows <paramref name="value"/> to the 7 significant digits that a <see cref="float"/> reliably holds.
    /// </summary>
    /// <exception cref="OverflowException">The value is not finite or is outside the range of <see cref="decimal"/>.</exception>
    public static decimal FromSingle(float value)
    {
        if (!float.IsFinite(value))
        {
            throw new OverflowException("Value was either too large or too small for a Decimal.");
        }

        return decimal.Parse(
            value.ToString("G7", CultureInfo.InvariantCulture),
            NumberStyles.Float,
            CultureInfo.InvariantCulture);
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
    /// Converts a <see cref="Exponential"/> to <see cref="decimal"/> using exact decimal arithmetic.
    /// </summary>
    /// <exception cref="OverflowException">The value is outside the range of <see cref="decimal"/>.</exception>
    public static decimal ToDecimal(Exponential value) =>
        RemoveNoise(value.Significand * PowerOfTen(value.Exponent));

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
}
