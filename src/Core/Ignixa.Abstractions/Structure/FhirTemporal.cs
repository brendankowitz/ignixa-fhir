// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Ignixa.Abstractions;

/// <summary>
/// A FHIR temporal value (<c>date</c>, <c>dateTime</c>, <c>instant</c> or <c>time</c>) that carries
/// the parsed value and the original wire literal at the same time.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DateTimeOffset"/> alone cannot represent a FHIR temporal value without loss: it has no
/// notion of partial precision, so <c>"1974"</c> and <c>"1974-01-01T00:00:00Z"</c> collapse onto the
/// same instant, and round-tripping the element back to the wire invents digits the source never had.
/// Keeping <see cref="Literal"/> alongside <see cref="Value"/> makes the value typed and lossless at
/// once.
/// </para>
/// <para>
/// Instances are only produced by <see cref="TryParse"/>. Malformed wire data is an expected input,
/// not a programmer error, so there is no public constructor that could yield a partially populated
/// instance.
/// </para>
/// </remarks>
[SuppressMessage("Microsoft.Design", "CA1036:OverrideMethodsOnComparableTypes", Justification = "FHIRPath ordering is tri-state, so a bool-returning relational operator cannot express an indeterminate result. Callers needing FHIRPath semantics use Compare; CompareTo exists only to give collections a total order.")]
public sealed class FhirTemporal : IEquatable<FhirTemporal>, IComparable<FhirTemporal>
{
    private const string TimeOnlyAnchor = "1900-01-01";

    private readonly DateTime _lowerBound;
    private readonly DateTime _upperBound;

    private FhirTemporal(
        string literal,
        FhirTemporalPrecision precision,
        FhirPrimitive kind,
        DateTimeOffset? value,
        DateTime lowerBound,
        DateTime upperBound)
    {
        Literal = literal;
        Precision = precision;
        Kind = kind;
        Value = value;
        _lowerBound = lowerBound;
        _upperBound = upperBound;
    }

    /// <summary>
    /// Gets the wire literal exactly as it was written, with any leading FHIRPath <c>@</c> sigil removed.
    /// </summary>
    public string Literal { get; }

    /// <summary>
    /// Gets the precision the literal was written at.
    /// </summary>
    /// <remarks>
    /// Precision is derived after a trailing all-zero fractional second is discarded, so
    /// <c>"2012-01-01T00:00:00.0"</c> reports <see cref="FhirTemporalPrecision.Second"/> while
    /// <see cref="Literal"/> still round-trips the <c>.0</c> verbatim. This matches how FHIRPath
    /// already treats those two literals as the same value.
    /// </remarks>
    public FhirTemporalPrecision Precision { get; }

    /// <summary>
    /// Gets the FHIR primitive type the literal came from.
    /// </summary>
    public FhirPrimitive Kind { get; }

    /// <summary>
    /// Gets the resolved instant, or <see langword="null"/> when the literal does not denote one.
    /// </summary>
    /// <remarks>
    /// This is <see langword="null"/> for <see cref="FhirTemporalPrecision.Year"/> and
    /// <see cref="FhirTemporalPrecision.Month"/> precision, because materialising a
    /// <see cref="DateTimeOffset"/> would fabricate a month and day the source never supplied, and for
    /// every <c>time</c> value, because a time of day is not a point on the calendar.
    /// </remarks>
    public DateTimeOffset? Value { get; }

    private bool IsTimeOnly => Kind == FhirPrimitive.Time;

    /// <summary>
    /// Determines whether two values denote the same instant at the same precision.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when both operands are equal.</returns>
    public static bool operator ==(FhirTemporal? left, FhirTemporal? right)
    {
        return left is null ? right is null : left.Equals(right);
    }

    /// <summary>
    /// Determines whether two values differ.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when the operands are not equal.</returns>
    public static bool operator !=(FhirTemporal? left, FhirTemporal? right)
    {
        return !(left == right);
    }

    /// <summary>
    /// Attempts to parse a FHIR temporal literal.
    /// </summary>
    /// <param name="literal">The wire literal, optionally prefixed with the FHIRPath <c>@</c> sigil.</param>
    /// <param name="kind">The FHIR primitive type the literal was read as.</param>
    /// <param name="result">The parsed value, or <see langword="null"/> when parsing failed.</param>
    /// <returns><see langword="true"/> when <paramref name="literal"/> was parsed.</returns>
    /// <remarks>
    /// <para>
    /// This never throws. Callers reading untrusted wire data fall back to the raw string on
    /// <see langword="false"/>, so an exception here would turn a recoverable data problem into a
    /// failed read.
    /// </para>
    /// <para>
    /// <paramref name="kind"/> is treated as schema-supplied metadata: the literal's shape is not
    /// validated against it, so <c>"2012"</c> parses successfully as
    /// <see cref="FhirPrimitive.Instant"/> and <c>"2012-01-01T10:30:00"</c> parses as
    /// <see cref="FhirPrimitive.Date"/>. Shape conformance checking belongs to
    /// <c>FhirPrimitiveValidator</c>.
    /// </para>
    /// </remarks>
    public static bool TryParse(string? literal, FhirPrimitive kind, out FhirTemporal? result)
    {
        result = null;

        if (string.IsNullOrEmpty(literal))
        {
            return false;
        }

        if (kind is not (FhirPrimitive.Date or FhirPrimitive.DateTime or FhirPrimitive.Instant or FhirPrimitive.Time))
        {
            return false;
        }

        var wire = literal[0] == '@' ? literal[1..] : literal;
        if (wire.Length == 0)
        {
            return false;
        }

        var normalized = Normalize(wire, kind);

        var precision = GetPrecision(normalized);
        if (precision == FhirTemporalPrecision.Invalid)
        {
            return false;
        }

        var lowerBound = GetLowerBound(normalized, precision);
        var upperBound = GetUpperBound(normalized, precision);
        if (lowerBound is null || upperBound is null)
        {
            return false;
        }

        result = new FhirTemporal(
            wire,
            precision,
            kind,
            ResolveValue(normalized, precision, kind),
            lowerBound.Value,
            upperBound.Value);

        return true;
    }

    /// <summary>
    /// Compares two temporal values using FHIRPath partial-precision ordering.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>
    /// A value less than zero when <paramref name="left"/> precedes <paramref name="right"/>, zero when
    /// they denote the same value, a value greater than zero when <paramref name="left"/> follows
    /// <paramref name="right"/>, or <see langword="null"/> when the ordering is indeterminate.
    /// </returns>
    /// <remarks>
    /// Each value denotes the interval its precision covers, so <c>@2012</c> spans the whole of 2012.
    /// Two overlapping intervals that are not identical have no defined order: <c>@2012 &gt; @2012-01</c>
    /// is neither true nor false, and FHIRPath requires that to surface as empty rather than as
    /// <c>false</c>. A <see langword="null"/> result is that empty.
    /// </remarks>
    public static int? Compare(FhirTemporal? left, FhirTemporal? right)
    {
        if (left is null || right is null)
        {
            return null;
        }

        if (left.IsTimeOnly != right.IsTimeOnly)
        {
            return null;
        }

        if (left._lowerBound == right._lowerBound && left._upperBound == right._upperBound)
        {
            return 0;
        }

        if (left._upperBound < right._lowerBound)
        {
            return -1;
        }

        if (left._lowerBound > right._upperBound)
        {
            return 1;
        }

        return null;
    }

    /// <summary>
    /// Determines whether this value denotes the same instant at the same precision as another.
    /// </summary>
    /// <param name="other">The value to compare against.</param>
    /// <returns><see langword="true"/> when the values are equal.</returns>
    /// <remarks>
    /// <see cref="Kind"/> is excluded so that a <c>date</c>, a <c>dateTime</c> and an <c>instant</c>
    /// carrying the same value compare equal, which matters because <c>dateTime</c> and <c>instant</c>
    /// share a wire format. <c>time</c> is the one exception, because a bare time of day is anchored to
    /// a placeholder date internally and must not collide with a real value on that date.
    /// </remarks>
    public bool Equals(FhirTemporal? other)
    {
        // Deliberately inconsistent with ToString(): two instances with different literals can be equal,
        // because "2012-01-01T10:00:00Z" and "2012-01-01T20:00:00+10:00" are the same instant. Do not
        // "fix" this by folding Literal into equality -- Literal exists to preserve wire fidelity, not to
        // establish identity, and FHIRPath equality is defined on the value rather than on its spelling.
        return other is not null
            && Precision == other.Precision
            && _lowerBound == other._lowerBound
            && IsTimeOnly == other.IsTimeOnly;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return Equals(obj as FhirTemporal);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Precision, _lowerBound, IsTimeOnly);
    }

    /// <summary>
    /// Orders this value against another for sorting purposes.
    /// </summary>
    /// <param name="other">The value to compare against.</param>
    /// <returns>A negative value, zero, or a positive value.</returns>
    /// <remarks>
    /// This is a total order so that sorted collections behave, and is therefore not FHIRPath comparison:
    /// it never reports indeterminacy. Use <see cref="Compare"/> for FHIRPath semantics.
    /// </remarks>
    public int CompareTo(FhirTemporal? other)
    {
        if (other is null)
        {
            return 1;
        }

        var byInstant = _lowerBound.CompareTo(other._lowerBound);
        if (byInstant != 0)
        {
            return byInstant;
        }

        var byPrecision = Precision.CompareTo(other.Precision);
        if (byPrecision != 0)
        {
            return byPrecision;
        }

        return IsTimeOnly.CompareTo(other.IsTimeOnly);
    }

    /// <summary>
    /// Returns the original wire literal.
    /// </summary>
    /// <returns>The value of <see cref="Literal"/>.</returns>
    /// <remarks>
    /// This is load-bearing rather than cosmetic: serializers, <c>toString()</c> in FHIRPath, and string
    /// coercion all route through here, and every one of them must reproduce the source spelling exactly.
    /// </remarks>
    public override string ToString()
    {
        return Literal;
    }

    private static string Normalize(string wire, FhirPrimitive kind)
    {
        var value = kind == FhirPrimitive.Time && !wire.StartsWith('T')
            ? "T" + wire
            : wire;

        return NormalizeMilliseconds(value);
    }

    private static string NormalizeMilliseconds(string value)
    {
        var timeZoneSuffix = string.Empty;
        var working = value;

        if (working.EndsWith('Z'))
        {
            timeZoneSuffix = "Z";
            working = working[..^1];
        }
        else
        {
            var timeIndex = working.IndexOf('T', StringComparison.Ordinal);
            var offsetIndex = Math.Max(working.LastIndexOf('+'), working.LastIndexOf('-'));
            if (timeIndex >= 0 && offsetIndex > timeIndex)
            {
                timeZoneSuffix = working[offsetIndex..];
                working = working[..offsetIndex];
            }
        }

        var fractionIndex = working.LastIndexOf('.');
        if (fractionIndex < 0)
        {
            return value;
        }

        foreach (var digit in working.AsSpan(fractionIndex + 1))
        {
            if (digit != '0')
            {
                return value;
            }
        }

        return string.Concat(working.AsSpan(0, fractionIndex), timeZoneSuffix);
    }

    private static FhirTemporalPrecision GetPrecision(string value)
    {
        if (value.StartsWith('T'))
        {
            return GetTimePrecision(value[1..]);
        }

        if (value.Length is >= 4 and <= 10)
        {
            return value.Split('-').Length switch
            {
                1 => FhirTemporalPrecision.Year,
                2 => FhirTemporalPrecision.Month,
                3 => FhirTemporalPrecision.Day,
                _ => FhirTemporalPrecision.Invalid
            };
        }

        var timeIndex = value.IndexOf('T', StringComparison.Ordinal);

        return timeIndex >= 0
            ? GetTimePrecision(StripTimeZone(value[(timeIndex + 1)..]))
            : FhirTemporalPrecision.Invalid;
    }

    private static FhirTemporalPrecision GetTimePrecision(string timePart)
    {
        var colons = 0;
        var hasFraction = false;

        foreach (var character in timePart)
        {
            if (character == ':')
            {
                colons++;
            }
            else if (character == '.')
            {
                hasFraction = true;
            }
        }

        if (colons == 0)
        {
            return FhirTemporalPrecision.Hour;
        }

        if (colons == 1)
        {
            return FhirTemporalPrecision.Minute;
        }

        return hasFraction ? FhirTemporalPrecision.Millisecond : FhirTemporalPrecision.Second;
    }

    private static string StripTimeZone(string timePart)
    {
        var trimmed = timePart.TrimEnd('Z');
        var offsetIndex = Math.Max(trimmed.LastIndexOf('+'), trimmed.LastIndexOf('-'));

        return offsetIndex >= 0 ? trimmed[..offsetIndex] : trimmed;
    }

    private static DateTimeOffset? ResolveValue(string normalized, FhirTemporalPrecision precision, FhirPrimitive kind)
    {
        if (kind == FhirPrimitive.Time || precision < FhirTemporalPrecision.Day)
        {
            return null;
        }

        return TryParseTemporal(normalized, out var value) ? value : null;
    }

    private static DateTime? GetLowerBound(string value, FhirTemporalPrecision precision)
    {
        switch (precision)
        {
            case FhirTemporalPrecision.Year:
                return TryParseYear(value, out var year)
                    ? new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    : null;

            case FhirTemporalPrecision.Month:
                return TryParseMonthStart(value, out var monthStart) ? monthStart : null;

            default:
                return TryParseTemporal(value, out var instant) ? instant.UtcDateTime : null;
        }
    }

    private static DateTime? GetUpperBound(string value, FhirTemporalPrecision precision)
    {
        switch (precision)
        {
            case FhirTemporalPrecision.Year:
                return TryParseYear(value, out var year)
                    ? new DateTime(year, 12, 31, 23, 59, 59, 999, DateTimeKind.Utc)
                    : null;

            case FhirTemporalPrecision.Month:
                return TryParseMonthStart(value, out var monthStart)
                    ? monthStart.AddMonths(1).AddMilliseconds(-1)
                    : null;

            default:
                if (!TryParseTemporal(value, out var parsed))
                {
                    return null;
                }

                var instant = parsed.UtcDateTime;

                return precision switch
                {
                    FhirTemporalPrecision.Day => instant.Date.AddDays(1).AddMilliseconds(-1),
                    FhirTemporalPrecision.Hour => instant.AddHours(1).AddMilliseconds(-1),
                    FhirTemporalPrecision.Minute => instant.AddMinutes(1).AddMilliseconds(-1),
                    FhirTemporalPrecision.Second => instant.AddSeconds(1).AddMilliseconds(-1),
                    _ => instant
                };
        }
    }

    private static bool TryParseYear(string value, out int year)
    {
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out year)
            && year is >= 1 and <= 9999;
    }

    private static bool TryParseMonthStart(string value, out DateTime monthStart)
    {
        return DateTime.TryParseExact(
            value + "-01",
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out monthStart);
    }

    private static bool TryParseTemporal(string value, out DateTimeOffset result)
    {
        var parseable = value.StartsWith('T') ? TimeOnlyAnchor + value : value;

        return DateTimeOffset.TryParse(
            parseable,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out result);
    }
}
