// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Ignixa.Abstractions;

/// <summary>
/// A FHIR temporal value (<c>date</c>, <c>dateTime</c>, <c>instant</c> or <c>time</c>) that carries
/// the wire literal and its parsed precision at the same time.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DateTimeOffset"/> alone cannot represent a FHIR temporal value without loss: it has no
/// notion of partial precision, so <c>"1974"</c> and <c>"1974-01-01T00:00:00Z"</c> collapse onto the
/// same instant, and round-tripping the element back to the wire invents digits the source never had.
/// <see cref="Literal"/> and <see cref="Precision"/> together make the value typed and lossless at
/// once.
/// </para>
/// <para>
/// There is deliberately no resolved-instant property. One existed and had no consumer outside its
/// own tests: it was <see langword="null"/> at <see cref="FhirTemporalPrecision.Year"/> and
/// <see cref="FhirTemporalPrecision.Month"/> precision and for every <c>time</c>, which is the same
/// ambiguous null this type exists to remove, and it collided with <c>IElement.Value</c> one
/// dereference away. Anything needing a <see cref="DateTimeOffset"/> has to say which one it means --
/// the lower bound, the upper bound, or a UTC normalisation -- so it belongs on a member named for
/// the answer it gives rather than on a bare <c>Value</c> whose meaning changes with precision.
/// </para>
/// <para>
/// Instances are only produced by <see cref="TryParse"/>. Malformed wire data is an expected input,
/// not a programmer error, so there is no public constructor that could yield a partially populated
/// instance.
/// </para>
/// </remarks>
[SuppressMessage("Microsoft.Design", "CA1036:OverrideMethodsOnComparableTypes", Justification = "FHIRPath ordering is tri-state, so a bool-returning relational operator cannot express an indeterminate result. Callers needing FHIRPath semantics use Compare; CompareTo exists only to give collections a total order. The non-generic IComparable is deliberately not implemented for the same reason: the evaluator has 'is IComparable' fallbacks that would silently turn FHIRPath's empty into a definite answer.")]
public sealed class FhirTemporal : IEquatable<FhirTemporal>, IComparable<FhirTemporal>
{
    private const string TimeOnlyAnchor = "1900-01-01";

    private readonly DateTime _lowerBound;
    private readonly DateTime _upperBound;

    private FhirTemporal(
        string literal,
        FhirTemporalPrecision precision,
        FhirPrimitive kind,
        DateTime lowerBound,
        DateTime upperBound,
        bool hasTimezone)
    {
        Literal = literal;
        Precision = precision;
        Kind = kind;
        _lowerBound = lowerBound;
        _upperBound = upperBound;
        HasTimezone = hasTimezone;
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
    /// Gets a value indicating whether the source literal carried a timezone (a <c>Z</c> or a
    /// <c>±hh:mm</c> offset) on its time-of-day component.
    /// </summary>
    /// <remarks>
    /// This is a genuine, observable fact about the wire value, and one no resolved instant could
    /// carry: a timezone-less <c>dateTime</c> denotes a floating local time, whereas a
    /// timezone-bearing one denotes a fixed instant, and FHIRPath treats a comparison between the two
    /// as indeterminate. Normalising to UTC erases exactly that distinction.
    /// It is derived by scanning the literal after the <c>T</c>, not from a parsed instant, because
    /// <see cref="DateTimeOffset"/> exposes no "an offset was present" flag, and not from
    /// <see cref="Kind"/>, which is unvalidated caller-supplied metadata that may disagree with the
    /// literal. A well-formed <c>date</c> has no time component and a well-formed <c>time</c> never
    /// carries a timezone, so both report <see langword="false"/> because the scan finds nothing, not
    /// because the kind suppresses it. That keeps this property consistent with the ordering keys,
    /// which are likewise derived from the literal.
    /// </remarks>
    public bool HasTimezone { get; }

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
    /// <para>
    /// <paramref name="kind"/> nonetheless selects an interpretation rather than merely labelling one:
    /// <see cref="FhirPrimitive.Time"/> anchors the literal to a placeholder date, because a time of
    /// day is not a point on the calendar. What it must never do is
    /// override a fact the literal already states. <see cref="HasTimezone"/> is read from the literal for
    /// that reason -- forcing it off for <see cref="FhirPrimitive.Date"/> once let a mislabelled
    /// <c>dateTime</c> report a floating local time while ordering as a fixed instant, and because
    /// <see cref="HasTimezone"/> is an equality and ordering key, that contradiction reached collections.
    /// </para>
    /// <para>
    /// Hour-only values are intentionally rejected even though <see cref="GetLiteralPrecision"/> classifies
    /// their shape. <see cref="FhirTemporal"/> models FHIR wire values, whose <c>dateTime</c> grammar
    /// requires minute precision; FHIRPath hour-only literals therefore remain untyped strings.
    /// </para>
    /// </remarks>
    public static bool TryParse(string? literal, FhirPrimitive kind, [NotNullWhen(true)] out FhirTemporal? result)
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
            lowerBound.Value,
            upperBound.Value,
            HasTimezoneComponent(normalized));

        return true;
    }

    private static bool HasTimezoneComponent(string normalized)
    {
        // Derived from the literal alone, never from Kind: Kind is unvalidated, so suppressing the scan
        // for date or time would let a mislabelled literal produce an instance whose HasTimezone
        // contradicts its own Value and ordering keys -- and HasTimezone participates in both equality
        // and CompareTo. The normalized form is used so that "13:45:00" and "T13:45:00" agree.

        // Only the substring after 'T' may hold a timezone; scanning the whole literal would mistake the
        // date part's '-' separators for a negative offset.
        var timeIndex = normalized.IndexOf('T', StringComparison.Ordinal);
        if (timeIndex < 0)
        {
            return false;
        }

        foreach (var character in normalized.AsSpan(timeIndex + 1))
        {
            if (character is 'Z' or '+' or '-')
            {
                return true;
            }
        }

        return false;
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
    /// <c>false</c>. A <see langword="null"/> result is that empty. The one exception is the second tier:
    /// FHIRPath treats seconds and milliseconds as a single precision, so two second-or-finer values
    /// compare as exact instants and always have a defined order.
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

        // Timezone-vs-no-timezone is indeterminate whenever both operands carry a time of day: a
        // floating local time could sit at any offset, so it overlaps a fixed instant rather than
        // ordering against it, and FHIRPath requires empty. This mirrors the evaluator's string
        // fallback (leftHasTz != rightHasTz => null). Gate on time-of-day presence (Precision >= Minute):
        // a date has no timezone by definition, so a HasTimezone difference between date-precision
        // values is meaningless, not a mismatch. Placed ahead of both comparison branches so it governs
        // the second-or-finer point comparison and the coarser interval comparison alike. Hour is a
        // structural classification only; TryParse intentionally does not construct hour-precision values.
        if (left.Precision >= FhirTemporalPrecision.Minute
            && right.Precision >= FhirTemporalPrecision.Minute
            && left.HasTimezone != right.HasTimezone)
        {
            return null;
        }

        // FHIRPath recognises exactly six precisions and stops at seconds: milliseconds are the
        // fractional part of the second tier, not a tier of their own. So once both operands are
        // second-precision-or-finer they compare as exact instants (points), never as intervals, which
        // is why @...:31 and @...:31.1 have a definite order instead of the empty that an interval
        // overlap would yield. This is deliberately kept separate from the lower/upper BOUND helpers:
        // a second-precision value still spans [ss.000, ss.999] for lowBoundary()/highBoundary(); only
        // comparison collapses it to its instant. _lowerBound is that instant (ss.000 for second
        // precision, ss.mmm for millisecond).
        if (left.Precision >= FhirTemporalPrecision.Second && right.Precision >= FhirTemporalPrecision.Second)
        {
            return left._lowerBound.CompareTo(right._lowerBound);
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
    /// <see cref="HasTimezone"/> <em>is</em> part of identity: a fixed instant and a floating local time
    /// are different values.
    /// <para>
    /// Two distinct comparison surfaces relate to this method and they do not hold the same invariant.
    /// <see cref="CompareTo"/> returns zero exactly when this method returns <see langword="true"/>, which
    /// is the consistency contract sorted and keyed collections require; it is why
    /// <see cref="HasTimezone"/> is a tiebreaker there as well as an equality key here.
    /// <see cref="Compare"/> carries no such invariant and must not be assumed to agree: it is tri-state,
    /// so a non-zero result there means "ordered", not "unequal", and a <see langword="null"/> result means
    /// the ordering is indeterminate rather than that the values differ. Use this method, never
    /// <see cref="Compare"/>, to decide equality.
    /// </para>
    /// <para>
    /// Equality is limited to <see cref="DateTime"/> tick resolution (100 ns). Fractional seconds with
    /// more precision can retain distinct <see cref="Literal"/> spellings while comparing equal when
    /// parsing rounds them to the same tick; <see cref="GetHashCode"/> intentionally follows that equality.
    /// </para>
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
            && IsTimeOnly == other.IsTimeOnly
            && HasTimezone == other.HasTimezone;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return Equals(obj as FhirTemporal);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Precision, _lowerBound, IsTimeOnly, HasTimezone);
    }

    /// <summary>
    /// Orders this value against another for sorting purposes.
    /// </summary>
    /// <param name="other">The value to compare against.</param>
    /// <returns>A negative value, zero, or a positive value.</returns>
    /// <remarks>
    /// This is a total order so that sorted collections behave, and is therefore not FHIRPath comparison:
    /// it never reports indeterminacy. Use <see cref="Compare"/> for FHIRPath semantics.
    /// The tiebreakers are exactly <see cref="Equals(FhirTemporal)"/>'s keys, so a zero result here means
    /// the operands are equal. Without that agreement a <see cref="SortedSet{T}"/> or an
    /// <c>OrderBy().Distinct()</c> would silently collapse two unequal values -- a timezone-bearing and a
    /// timezone-less reading of the same clock face, most obviously -- into one.
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

        var byTimeOnly = IsTimeOnly.CompareTo(other.IsTimeOnly);
        if (byTimeOnly != 0)
        {
            return byTimeOnly;
        }

        // Arbitrary but total, and it is what makes CompareTo == 0 agree with Equals == true. Sorting the
        // timezone-less value first has no temporal meaning -- the two are genuinely unordered in FHIRPath
        // terms; the only requirement here is that they do not collide.
        return HasTimezone.CompareTo(other.HasTimezone);
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

    /// <summary>
    /// Classifies the precision of a temporal literal by its structural shape, without constructing a
    /// full <see cref="FhirTemporal"/> and without implying that the literal is valid or parseable.
    /// </summary>
    /// <param name="literal">The wire literal, optionally prefixed with the FHIRPath <c>@</c> sigil.</param>
    /// <returns>
    /// The inferred precision, or <see cref="FhirTemporalPrecision.Invalid"/> when <paramref name="literal"/> is
    /// <see langword="null"/>, empty, or does not match any recognised temporal shape.
    /// </returns>
    /// <remarks>
    /// This method classifies shape only: it may return a non-<see cref="FhirTemporalPrecision.Invalid"/> value
    /// for inputs that <see cref="TryParse"/> would reject — for example, <c>"2012-01-01T10"</c>
    /// (hour-only, which is not a valid FHIR dateTime) returns <see cref="FhirTemporalPrecision.Hour"/> here.
    /// Use <see cref="TryParse"/> when validity is required.
    /// </remarks>
    internal static FhirTemporalPrecision GetLiteralPrecision(string? literal)
    {
        if (string.IsNullOrEmpty(literal))
        {
            return FhirTemporalPrecision.Invalid;
        }

        var wire = literal[0] == '@' ? literal[1..] : literal;
        return wire.Length == 0 ? FhirTemporalPrecision.Invalid : GetPrecision(wire);
    }

    /// <summary>
    /// Returns a value indicating whether <paramref name="literal"/> looks like any FHIR or FHIRPath temporal
    /// value (<c>date</c>, <c>dateTime</c>, <c>instant</c>, or <c>time</c>).
    /// </summary>
    /// <param name="literal">The string to test, optionally prefixed with the FHIRPath <c>@</c> sigil.</param>
    /// <returns>
    /// <see langword="true"/> for an <c>@</c>-prefixed FHIRPath literal, a <c>T</c>-prefixed time-only literal,
    /// or a year-first date or dateTime literal.
    /// </returns>
    /// <remarks>
    /// This is a structural heuristic: it tests character-level shape only and implies nothing about
    /// whether the string is a valid FHIR value. Use <see cref="TryParse"/> when validity is required.
    /// </remarks>
    internal static bool IsTemporalLiteral(string? literal)
    {
        if (string.IsNullOrEmpty(literal))
        {
            return false;
        }

        // FHIRPath @-sigil marks any temporal literal.
        if (literal[0] == '@')
        {
            return true;
        }

        // Time-only literal: T followed by a digit.
        if (literal[0] == 'T' && literal.Length >= 2 && char.IsDigit(literal[1]))
        {
            return true;
        }

        // Date or dateTime: starts with a 4-digit year, followed by end-of-string, '-', or 'T'/'space' separator.
        return literal.Length >= 4
            && char.IsDigit(literal[0]) && char.IsDigit(literal[1])
            && char.IsDigit(literal[2]) && char.IsDigit(literal[3])
            && (literal.Length == 4 || literal[4] == '-' || literal[4] == 'T' || literal[4] == ' ');
    }

    /// <summary>
    /// Returns a value indicating whether <paramref name="literal"/> looks like a FHIR <c>date</c> or
    /// <c>dateTime</c> literal (year-first format).
    /// </summary>
    /// <param name="literal">The string to test, optionally prefixed with the FHIRPath <c>@</c> sigil.</param>
    /// <returns>
    /// <see langword="true"/> when the literal begins with a 4-digit year optionally followed by
    /// <c>-</c>, <c>T</c>, or space separators. Returns <see langword="false"/> for time-only strings
    /// (those starting with <c>T</c>) so that the caller can distinguish date/dateTime from time values.
    /// </returns>
    /// <remarks>
    /// This is a structural heuristic: it tests character-level shape only and implies nothing about
    /// whether the string is a valid FHIR value. Use <see cref="TryParse"/> when validity is required.
    /// Unlike <see cref="IsTemporalLiteral"/>, this method returns <see langword="false"/> for
    /// <c>T</c>-prefixed time-only literals, making it suitable for code paths that handle date and time
    /// strings in separate branches.
    /// </remarks>
    internal static bool IsDateOrDateTimeLiteral(string? literal)
    {
        if (string.IsNullOrEmpty(literal))
        {
            return false;
        }

        var wire = literal[0] == '@' ? literal[1..] : literal;

        return wire.Length >= 4
            && char.IsDigit(wire[0]) && char.IsDigit(wire[1])
            && char.IsDigit(wire[2]) && char.IsDigit(wire[3])
            && (wire.Length == 4 || wire[4] == '-' || wire[4] == 'T' || wire[4] == ' ');
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

    internal static DateTime? GetLowerBound(string value, FhirTemporalPrecision precision)
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

    internal static DateTime? GetUpperBound(string value, FhirTemporalPrecision precision)
    {
        switch (precision)
        {
            case FhirTemporalPrecision.Year:
                return TryParseYear(value, out var year)
                    ? new DateTime(year, 12, 31, 23, 59, 59, 999, DateTimeKind.Utc)
                    : null;

            case FhirTemporalPrecision.Month:
                return TryParseMonthStart(value, out var monthStart)
                    ? EndOfInterval(monthStart, TimeSpan.FromDays(DateTime.DaysInMonth(monthStart.Year, monthStart.Month)))
                    : null;

            default:
                if (!TryParseTemporal(value, out var parsed))
                {
                    return null;
                }

                var instant = parsed.UtcDateTime;

                return precision switch
                {
                    FhirTemporalPrecision.Day => EndOfInterval(instant.Date, TimeSpan.FromDays(1)),
                    FhirTemporalPrecision.Hour => EndOfInterval(instant, TimeSpan.FromHours(1)),
                    FhirTemporalPrecision.Minute => EndOfInterval(instant, TimeSpan.FromMinutes(1)),
                    FhirTemporalPrecision.Second => EndOfInterval(instant, TimeSpan.FromSeconds(1)),
                    _ => instant
                };
        }
    }

    /// <summary>
    /// Returns the last representable instant of the interval starting at <paramref name="start"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written as one addition rather than as <c>AddDays(1).AddMilliseconds(-1)</c> because the
    /// intermediate is the value that overflows. <c>9999-12-31</c> is a common open-ended-period
    /// sentinel and a valid FHIR date, yet stepping a day past it to come back a millisecond throws
    /// <see cref="ArgumentOutOfRangeException"/> from inside <see cref="TryParse"/> - which documents
    /// that it never throws, and whose callers are reading untrusted wire data. The same applied to
    /// <c>9999-12</c> and to <c>9999-12-31T23:59:59Z</c>.
    /// </para>
    /// <para>
    /// Saturating is the answer rather than refusing the literal: the interval's true end
    /// (<c>9999-12-31T23:59:59.999</c>) is representable and correct; only the route to it was not.
    /// Refusing would have made a conformant date fall back to a raw string.
    /// </para>
    /// </remarks>
    private static DateTime EndOfInterval(DateTime start, TimeSpan length)
    {
        return length > DateTime.MaxValue - start
            ? new DateTime(9999, 12, 31, 23, 59, 59, 999, start.Kind)
            : start + length - TimeSpan.FromMilliseconds(1);
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
