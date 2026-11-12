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
/// <see cref="ToDecimal(Exponential, bool)"/> evaluates the same product with an exact power of ten, which is what the
/// former cast produced. The binary noise that the library's unit factors carry on .NET 11 is removed from the
/// factor of an operation: each unit factor (<see cref="RemoveFactorNoise"/>) and then the combined source/target
/// ratio or product (<see cref="RemoveCombinedFactorNoise"/>), never from a value or a result, so long quotients keep
/// all 28 digits on every runtime. The intended differences on .NET 11 are that trailing zeros of a result computed
/// from a cleaned factor are dropped (<c>2.0 'cm' * 2.0 'm'</c> is <c>0.04</c>, not <c>0.040000</c>), and that a
/// result whose factor was noisy on .NET 10 can be more exact (<c>100 'm/s'</c> is <c>360 'km/h'</c>); a factor that
/// is genuinely longer than 15 digits (<c>km/h</c>) cannot be cleaned and keeps its residual error.
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
    /// power of ten, matching Fhir.Metrics' own formula before .NET 11. The significand is never rounded, so long
    /// quotients such as <c>1/3</c> keep all 28 digits.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="dropTrailingZeros">
    /// Drops trailing zeros on runtimes where the cast is correctly rounded (.NET 11+), where the operands of the
    /// calculation had their unit factor cleaned by <see cref="RemoveFactorNoise"/>. Earlier runtimes never produce those
    /// zeros from a cleaned operand, so the flag has no effect there.
    /// </param>
    /// <exception cref="OverflowException">The value is outside the range of <see cref="decimal"/>.</exception>
    public static decimal ToDecimal(Exponential value, bool dropTrailingZeros = false)
    {
        var result = value.Significand * PowerOfTen(value.Exponent);
        return dropTrailingZeros && CastIsCorrectlyRounded ? result / TrailingZeroStripper : result;
    }

    // Probed at run time rather than compile time so that the right behaviour is chosen for the runtime
    // that actually executes the cast inside Fhir.Metrics.
    private static readonly bool CastIsCorrectlyRounded = (decimal)Math.Pow(10.0, -2) != 0.01m;

    /// <summary>
    /// Whether Fhir.Metrics leaves binary noise in the factors of its unit conversions on this runtime
    /// (.NET 11+), so that canonical quantities need <see cref="RemoveFactorNoise"/>.
    /// </summary>
    public static bool UnitFactorsCarryNoise => CastIsCorrectlyRounded;

    // A few chained double casts, each with a relative rounding error of at most 1.1e-16. The noisiest factor of
    // the UCUM units covered by the test sweep (dL, [gal_us], [pt_us]) is 3.1e-16 off.
    private const decimal FactorNoiseTolerance = 5e-16m;

    /// <summary>
    /// Removes the binary noise from the significand of a <em>unit factor</em> such as the canonical form of
    /// <c>1 cm</c> (<c>1.0000000000000000208...e-2</c>), by rounding it to 15 significant digits when that moves
    /// it by no more than <see cref="FactorNoiseTolerance"/> (relative). Returns the factor unchanged otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The noise comes from the unit factors (<c>Power</c> in Fhir.Metrics casts a double), so it is cleaned on the
    /// factor, which is a fixed property of the unit, and never on a user value or on a result. The calculation then
    /// multiplies the clean factor by the exact value, and the later multiplication or division is exact significand
    /// arithmetic, so <c>1 'm' / 3 'm'</c> keeps all 28 digits. Factors that genuinely need more than 15 digits,
    /// such as the 1/3.6 of <c>km/h</c>, lie further from the 15 digit grid than the tolerance and are kept as they are.
    /// </para>
    /// <para>
    /// Residual risk: a unit factor that is genuinely longer than 15 digits and happens to lie within the tolerance of
    /// a 15 digit value is rounded.
    /// </para>
    /// </remarks>
    public static decimal RemoveFactorNoise(decimal factor) => Snap(factor, FactorNoiseTolerance);

    // What is left in the ratio or product of two factors that were each cleaned: compound units such as mL/min
    // (1e-6 / 60) have a genuinely long factor that cannot be cleaned on its own, and its binary noise (at most
    // 3e-17 for the units in the test sweep) shows up again when two such factors are combined. The window is
    // much tighter than FactorNoiseTolerance because genuinely long combined factors are common (L to [tsp_us]
    // lies 8e-17 from its 15 digit value, 1/12 of in/ft 4e-16) and must not be snapped.
    private const decimal CombinedFactorNoiseTolerance = 5e-17m;

    /// <summary>
    /// Removes the binary noise that remains in the combined factor of an operation, i.e. the ratio of the source and
    /// target factor of a conversion or division, or the product of the factors of a multiplication. A combined factor
    /// such as the 1000 between <c>L/min</c> and <c>mL/min</c> must be exact, otherwise <c>1 'L/min'</c> and
    /// <c>1000 'mL/min'</c> are not equal. Uses a tighter tolerance than <see cref="RemoveFactorNoise"/> (see
    /// <see cref="CombinedFactorNoiseTolerance"/>) and has the same residual risk.
    /// </summary>
    public static decimal RemoveCombinedFactorNoise(decimal factor) => Snap(factor, CombinedFactorNoiseTolerance);

    private static decimal Snap(decimal factor, decimal tolerance)
    {
        if (!CastIsCorrectlyRounded || factor == 0m)
        {
            return factor;
        }

        var rounded = RoundToDoubleDigits(factor);
        if (rounded == factor || Math.Abs(rounded - factor) > Math.Abs(factor) * tolerance)
        {
            return factor;
        }

        return rounded / TrailingZeroStripper;
    }

    private static decimal RoundToDoubleDigits(decimal factor)
    {
        var magnitude = (int)Math.Floor(Math.Log10((double)Math.Abs(factor)));
        var decimals = Math.Min(DoubleSignificantDigits - 1 - magnitude, MaxDecimalScale);

        if (decimals >= 0)
        {
            return decimal.Round(factor, decimals, MidpointRounding.AwayFromZero);
        }

        var scale = PowerOfTen(-decimals);
        return decimal.Round(factor / scale, 0, MidpointRounding.AwayFromZero) * scale;
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
