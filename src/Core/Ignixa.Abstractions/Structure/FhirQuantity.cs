// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;

namespace Ignixa.Abstractions;

/// <summary>
/// A numeric value paired with a UCUM unit or a FHIRPath calendar-duration keyword.
/// </summary>
/// <remarks>
/// <para>
/// This is the single carrier for a quantity value across Ignixa, and it lives here for the same
/// reason <see cref="FhirTemporal"/> does: <c>IElement.Value</c> is declared in this assembly, so
/// every producer of an element value — the FHIRPath evaluator, the JSON element readers, the
/// Firely interop shims — has to be able to name the type it puts in there without taking a
/// dependency on any of the others. A second quantity type in any one of those layers would not be
/// a duplicate representation so much as a value that the engines silently fail to recognise:
/// callers reach a quantity by testing <c>element.Value is FhirQuantity</c>, so a near-identical
/// type from a different assembly matches nothing and turns every comparison into an empty
/// collection rather than an error.
/// </para>
/// <para>
/// The type carries data and value semantics only. Unit algebra — conversion, compatibility,
/// arithmetic — is FHIRPath evaluation policy and lives with the evaluator, behind
/// <c>IQuantityUnitConverter</c> in <c>Ignixa.FhirPath</c>. That split is what keeps this assembly
/// free of a UCUM implementation and free of any opinion about how two quantities combine.
/// </para>
/// <para>
/// There is deliberately no separate precision member. <see cref="FhirTemporal"/> needs one because
/// a <see cref="DateTimeOffset"/> cannot express "year only", but a <see cref="decimal"/> carries
/// its own scale, so <c>1.50 'mg'</c> and <c>1.5 'mg'</c> are already distinguishable through
/// <see cref="Value"/> alone and round-trip through <see cref="ToString"/> unchanged. Precision is
/// not part of equality, per FHIRPath, which defines <c>=</c> on quantities by value and unit and
/// leaves precision to <c>~</c>.
/// </para>
/// </remarks>
public sealed class FhirQuantity : IEquatable<FhirQuantity>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FhirQuantity"/> class.
    /// </summary>
    /// <param name="value">The numeric value.</param>
    /// <param name="unit">The UCUM unit code, or a FHIRPath calendar-duration keyword.</param>
    /// <exception cref="ArgumentNullException"><paramref name="unit"/> is <see langword="null"/>.</exception>
    public FhirQuantity(decimal value, string unit)
    {
        ArgumentNullException.ThrowIfNull(unit);

        Value = value;
        Unit = unit;
    }

    /// <summary>
    /// Gets the numeric value, whose <see cref="decimal"/> scale is the quantity's precision.
    /// </summary>
    public decimal Value { get; }

    /// <summary>
    /// Gets the UCUM unit code (for example <c>mg</c>, <c>Cel</c>, <c>mm[Hg]</c>) or the FHIRPath
    /// calendar-duration keyword (for example <c>week</c>).
    /// </summary>
    /// <remarks>
    /// The two forms are kept distinct rather than normalised onto one another, because they are not
    /// the same concept: a calendar keyword is calendar-aware, so <c>1 year</c> spans a leap day when
    /// one falls inside it, whereas the UCUM code <c>'a'</c> is a fixed 365.25-day duration.
    /// </remarks>
    public string Unit { get; }

    /// <summary>
    /// Determines whether two quantities have the same value and unit.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when both operands are equal.</returns>
    public static bool operator ==(FhirQuantity? left, FhirQuantity? right)
    {
        return left is null ? right is null : left.Equals(right);
    }

    /// <summary>
    /// Determines whether two quantities differ.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when the operands are not equal.</returns>
    public static bool operator !=(FhirQuantity? left, FhirQuantity? right)
    {
        return !(left == right);
    }

    /// <summary>
    /// Determines whether this quantity has the same value and unit as another.
    /// </summary>
    /// <param name="other">The quantity to compare against.</param>
    /// <returns><see langword="true"/> when the quantities are equal.</returns>
    /// <remarks>
    /// Units are compared as written, with no unit conversion and no normalisation of calendar
    /// keywords onto their UCUM codes. Converting here would make equality depend on a UCUM
    /// implementation this assembly does not have, and would fold together the two genuinely
    /// different concepts described on <see cref="Unit"/>. Callers wanting <c>7 'd' = 1 'wk'</c> to
    /// hold convert to a common unit first, which is what the FHIRPath evaluator does.
    /// </remarks>
    public bool Equals(FhirQuantity? other)
    {
        return other is not null
            && Value == other.Value
            && string.Equals(Unit, other.Unit, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return Equals(obj as FhirQuantity);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Value, Unit);
    }

    /// <summary>
    /// Returns the FHIRPath literal form of this quantity.
    /// </summary>
    /// <returns>
    /// A calendar-duration literal such as <c>1 week</c> when <see cref="Unit"/> is a FHIRPath
    /// keyword, otherwise a quoted UCUM literal such as <c>1 'wk'</c>.
    /// </returns>
    /// <remarks>
    /// This is load-bearing rather than cosmetic: FHIRPath's <c>toString()</c> and string coercion
    /// route through here, and both must reproduce the value at its original precision, which is why
    /// the value is formatted from its <see cref="decimal"/> scale rather than through the default
    /// <see cref="decimal.ToString()"/>.
    /// </remarks>
    public override string ToString()
    {
        var formattedValue = FormatAtScale(Value);

        return IsCalendarKeyword(Unit)
            ? $"{formattedValue} {Unit}"
            : $"{formattedValue} '{Unit}'";
    }

    /// <summary>
    /// Formats a decimal preserving its trailing zeros, which carry the value's stated precision.
    /// </summary>
    private static string FormatAtScale(decimal value)
    {
        var scale = (decimal.GetBits(value)[3] >> 16) & 0xFF;

        return scale == 0
            ? value.ToString(CultureInfo.InvariantCulture)
            : value.ToString("0." + new string('0', scale), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Returns a value indicating whether the unit is a FHIRPath calendar-duration keyword rather
    /// than a UCUM code.
    /// </summary>
    private static bool IsCalendarKeyword(string unit) => unit switch
    {
        "year" or "years" or "month" or "months" or "week" or "weeks" or
        "day" or "days" or "hour" or "hours" or "minute" or "minutes" or
        "second" or "seconds" or "millisecond" or "milliseconds" => true,
        _ => false,
    };
}
