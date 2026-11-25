// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Numerics;
using System.Text;

namespace Ignixa.SqlOnFhir.Fabric;

/// <summary>
/// An exact Delta <c>decimal(p,s)</c> value: an unscaled integer at a fixed scale. Built from JSON number text so
/// that no digit passes through <see cref="decimal"/> or <see cref="double"/>.
/// </summary>
/// <remarks>
/// A source value with non-zero digits beyond the column scale is rejected instead of rounded, a value needing more
/// than the column precision is rejected instead of becoming null, and a non-zero value whose effective exponent
/// (after the fraction digits are folded in) exceeds ±100 is rejected even when trailing zeros of the digits would
/// cancel it; all three are deterministic policy.
/// </remarks>
internal readonly struct SilverDecimal
{
    // Exponents beyond ±this are rejected (policy), even when trailing zeros would cancel them; the bound is far
    // outside every decimal(38, s) column, so only such zero-padded spellings are affected.
    private const int MaxMeaningfulExponent = 100;

    private readonly BigInteger _unscaled;
    private readonly int _scale;

    private SilverDecimal(BigInteger unscaled, int scale)
    {
        _unscaled = unscaled;
        _scale = scale;
    }

    /// <summary>
    /// Reads a JSON number token's text into a column of the given precision and scale. The text has already been
    /// accepted by the JSON parser, so its grammar is not re-checked.
    /// </summary>
    public static bool TryParse(string jsonNumber, int precision, int scale, out SilverDecimal value)
    {
        value = default;
        Split(jsonNumber, out var negative, out var digits, out var exponent);
        digits = digits.TrimStart('0');
        if (digits.Length == 0)
        {
            value = new SilverDecimal(BigInteger.Zero, scale);
            return true;
        }

        if (Math.Abs(exponent) > MaxMeaningfulExponent)
        {
            return false;
        }

        // digits × 10^exponent, rescaled to `scale` fraction digits.
        var shift = (int)exponent + scale;
        if (shift < 0)
        {
            // digits starts with a non-zero digit, so dropping all of them always drops a non-zero digit.
            if (-shift >= digits.Length || digits[^(-shift)..].Any(digit => digit != '0'))
            {
                return false;
            }

            digits = digits[..^(-shift)];
        }
        else
        {
            digits += new string('0', shift);
        }

        if (digits.Length > precision)
        {
            return false;
        }

        var unscaled = BigInteger.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
        value = new SilverDecimal(negative ? -unscaled : unscaled, scale);
        return true;
    }

    /// <summary>Reads a value already in row encoding (plain notation with exactly the column scale).</summary>
    public static SilverDecimal FromRowText(string rowText, int scale)
    {
        var negative = rowText.StartsWith('-');
        var digits = rowText.TrimStart('-').Replace(".", string.Empty, StringComparison.Ordinal);
        var unscaled = BigInteger.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
        return new SilverDecimal(negative ? -unscaled : unscaled, scale);
    }

    /// <summary>The row encoding: plain notation with exactly <c>scale</c> fraction digits.</summary>
    public string ToRowText()
    {
        var digits = BigInteger.Abs(_unscaled).ToString(CultureInfo.InvariantCulture).PadLeft(_scale + 1, '0');
        var builder = new StringBuilder(digits.Length + 2);
        if (_unscaled.Sign < 0)
        {
            builder.Append('-');
        }

        builder.Append(digits, 0, digits.Length - _scale);
        if (_scale > 0)
        {
            builder.Append('.').Append(digits, digits.Length - _scale, _scale);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The companion encoding: the canonical string of an arbitrary-precision decimal on the JVM, which switches to
    /// scientific notation when the adjusted exponent is below -6 (so zero at scale 18 is <c>0E-18</c>).
    /// </summary>
    public string ToCompanionText()
    {
        var coefficient = BigInteger.Abs(_unscaled).ToString(CultureInfo.InvariantCulture);
        var adjusted = -_scale + (coefficient.Length - 1);
        var sign = _unscaled.Sign < 0 ? "-" : string.Empty;
        if (adjusted >= -6)
        {
            return sign + new SilverDecimal(BigInteger.Abs(_unscaled), _scale).ToRowText();
        }

        var mantissa = coefficient.Length == 1 ? coefficient : coefficient[..1] + "." + coefficient[1..];
        return sign + mantissa + "E" + adjusted.ToString(CultureInfo.InvariantCulture);
    }

    private static void Split(string text, out bool negative, out string digits, out long exponent)
    {
        negative = text.StartsWith('-');
        var body = negative ? text[1..] : text;
        var exponentAt = body.IndexOfAny(['e', 'E']);
        var mantissa = exponentAt < 0 ? body : body[..exponentAt];
        exponent = exponentAt < 0 ? 0 : ReadExponent(body[(exponentAt + 1)..]);

        var point = mantissa.IndexOf('.', StringComparison.Ordinal);
        var whole = point < 0 ? mantissa : mantissa[..point];
        var trimmedFraction = point < 0 ? string.Empty : mantissa[(point + 1)..].TrimEnd('0');
        digits = whole + trimmedFraction;
        exponent -= trimmedFraction.Length;
    }

    private static long ReadExponent(string text)
    {
        // Saturation happens before the fraction length is subtracted, so the bound must exceed any text length
        // (< 2^31) by far more than MaxMeaningfulExponent: a saturated exponent then stays out of range either way.
        var magnitude = text.TrimStart('+', '-').TrimStart('0');
        var value = magnitude.Length > 12 ? 1_000_000_000_000L : long.Parse("0" + magnitude, NumberStyles.None, CultureInfo.InvariantCulture);
        return text.StartsWith('-') ? -value : value;
    }
}
