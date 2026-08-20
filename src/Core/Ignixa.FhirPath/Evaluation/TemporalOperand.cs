/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * The single place the engine decides whether an operand is a temporal, how to type it, and whether two
 * temporals are equal.
 */

using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// Recognises temporal operands and answers FHIRPath equality for them.
/// </summary>
/// <remarks>
/// <para>
/// FHIRPath temporal literals evaluate to a plain <see cref="string"/> carrying a <c>date</c>,
/// <c>dateTime</c>, <c>instant</c> or <c>time</c> instance type, while values read out of a resource
/// arrive as a typed <see cref="FhirTemporal"/>. Any code that inspects only one of those two
/// representations answers a different question than the code that inspects the other, which is how the
/// singleton <c>=</c> operator and the collection functions came to disagree: <c>=</c> resolved both
/// operands to <see cref="FhirTemporal"/> and compared instants, whereas <c>distinct()</c>, <c>in</c>,
/// <c>contains</c>, <c>intersect</c>, <c>exclude</c> and <c>|</c> compared the wire literals as text.
/// <c>@2012-01-01T10:00:00Z = @2012-01-01T20:00:00+10:00</c> was therefore <see langword="true"/> as an
/// operator and <see langword="false"/> as collection membership.
/// </para>
/// <para>
/// Both surfaces now call <see cref="AreEqual"/>, so they cannot drift again.
/// </para>
/// </remarks>
internal static class TemporalOperand
{
    /// <summary>
    /// Determines whether an operand should be compared as a temporal value.
    /// </summary>
    /// <param name="value">The operand's value.</param>
    /// <param name="instanceType">The operand's declared instance type, in any casing.</param>
    /// <returns><see langword="true"/> when the operand is a temporal.</returns>
    /// <remarks>
    /// The declared type is authoritative for FHIRPath literals, whose values are plain strings, but it is
    /// not sufficient on its own: an element carrying a <see cref="FhirTemporal"/> is a temporal regardless
    /// of what its instance type says. Testing the value as well keeps the comparison paths from
    /// disagreeing when a typed value arrives with a type name the gate does not enumerate.
    /// </remarks>
    public static bool IsTemporal(object? value, string? instanceType)
    {
        if (value is FhirTemporal)
        {
            return true;
        }

        return instanceType is not null
            && (instanceType.Equals("date", StringComparison.OrdinalIgnoreCase)
                || instanceType.Equals("dateTime", StringComparison.OrdinalIgnoreCase)
                || instanceType.Equals("instant", StringComparison.OrdinalIgnoreCase)
                || instanceType.Equals("time", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves an operand to a <see cref="FhirTemporal"/>, or <see langword="null"/> when it is not one.
    /// </summary>
    /// <param name="value">The operand's value.</param>
    /// <param name="instanceType">The operand's declared instance type, used to type a literal string.</param>
    /// <returns>The typed temporal, or <see langword="null"/>.</returns>
    public static FhirTemporal? AsTemporal(object? value, string? instanceType)
    {
        switch (value)
        {
            case FhirTemporal temporal:
                return temporal;
            case string text:
                return FhirTemporal.TryParse(text, InferKind(text, instanceType), out var parsed) ? parsed : null;
            case DateTime dateTime:
                return FhirTemporal.TryParse(dateTime.ToString("o"), FhirPrimitive.DateTime, out var fromDateTime) ? fromDateTime : null;
            case DateTimeOffset dateTimeOffset:
                return FhirTemporal.TryParse(dateTimeOffset.ToString("o"), FhirPrimitive.DateTime, out var fromOffset) ? fromOffset : null;
            default:
                return null;
        }
    }

    /// <summary>
    /// Applies FHIRPath equality to two temporals.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>
    /// <see langword="true"/> or <see langword="false"/> when equality is decidable, and
    /// <see langword="null"/> when partial precision or a timezone mismatch makes it indeterminate.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is built on <see cref="FhirTemporal.Compare"/>, not on <see cref="FhirTemporal.Equals"/>, and
    /// the distinction is load-bearing. <see cref="FhirTemporal.Equals"/> is identity: it folds
    /// <c>Precision</c> into the key so that a value and a sorted collection of values behave, which makes
    /// <c>@2012-01-01T10:00:30</c> unequal to <c>@2012-01-01T10:00:30.000</c>. FHIRPath disagrees -
    /// milliseconds are the fractional part of the second tier rather than a tier of their own, so those
    /// two are the same value - and <see cref="FhirTemporal.Compare"/> is the surface that encodes that.
    /// Using <see cref="FhirTemporal.Equals"/> for collection membership would therefore have replaced one
    /// disagreement between <c>=</c> and <c>distinct()</c> with a narrower one at millisecond precision.
    /// </para>
    /// <para>
    /// A time of day and a calendar value are different types rather than overlapping intervals, so
    /// equality between them is decidably <see langword="false"/> where ordering is indeterminate (official
    /// <c>testDateNotEqualTime*</c>). <see cref="FhirTemporal.Compare"/> cannot express that distinction -
    /// it returns <see langword="null"/> for both - which is why the check sits here rather than in
    /// <see cref="FhirTemporal"/>.
    /// </para>
    /// </remarks>
    public static bool? AreEqual(FhirTemporal left, FhirTemporal right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if ((left.Kind == FhirPrimitive.Time) != (right.Kind == FhirPrimitive.Time))
        {
            return false;
        }

        return FhirTemporal.Compare(left, right) switch
        {
            null => null,
            0 => true,
            _ => false
        };
    }

    /// <summary>
    /// Decides membership of one temporal in a collection, where FHIRPath has no third state.
    /// </summary>
    /// <param name="left">The candidate.</param>
    /// <param name="right">The element being tested against.</param>
    /// <returns><see langword="true"/> only when the two are decidably equal.</returns>
    /// <remarks>
    /// <c>distinct()</c>, <c>in</c>, <c>contains</c>, <c>intersect</c>, <c>exclude</c> and <c>|</c> all
    /// answer a boolean, so the indeterminate case has to collapse. It collapses to "not the same item":
    /// membership asserts that an equal item is present, and an indeterminate comparison is not that
    /// assertion. Collapsing the other way would let <c>@2012</c> and <c>@2012-01</c> - values the engine
    /// declines to call equal - silently deduplicate each other.
    /// </remarks>
    public static bool AreSameItem(FhirTemporal left, FhirTemporal right) => AreEqual(left, right) == true;

    private static FhirPrimitive InferKind(string literal, string? instanceType)
    {
        if (string.Equals(instanceType, "date", StringComparison.OrdinalIgnoreCase))
            return FhirPrimitive.Date;
        if (string.Equals(instanceType, "dateTime", StringComparison.OrdinalIgnoreCase))
            return FhirPrimitive.DateTime;
        if (string.Equals(instanceType, "instant", StringComparison.OrdinalIgnoreCase))
            return FhirPrimitive.Instant;
        if (string.Equals(instanceType, "time", StringComparison.OrdinalIgnoreCase))
            return FhirPrimitive.Time;

        var wire = literal.Length > 0 && literal[0] == '@' ? literal[1..] : literal;
        if (wire.StartsWith('T') || (wire.Contains(':', StringComparison.Ordinal) && !wire.Contains('-', StringComparison.Ordinal)))
            return FhirPrimitive.Time;

        return wire.Contains('T', StringComparison.Ordinal) ? FhirPrimitive.DateTime : FhirPrimitive.Date;
    }
}
