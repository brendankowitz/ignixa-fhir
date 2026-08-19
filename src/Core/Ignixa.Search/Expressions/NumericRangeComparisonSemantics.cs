// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Extensions;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Expressions;

/// <summary>
/// The FHIR number and quantity search prefix table, stated once.
/// </summary>
/// <remarks>
/// <para>
/// The numeric sibling of <see cref="DateRangeComparisonSemantics"/>. The specification
/// (https://hl7.org/fhir/R4/search.html#prefix) defines every prefix as a relation between two intervals —
/// the range of the search value and the range of the target value — and that definition is uniform across
/// range-valued types, so it is written here the same way and backends render the result rather than
/// re-deriving it:
/// </para>
/// <para>
/// <c>eq</c> "the range of the search value fully contains the range of the target value";
/// <c>ne</c> "the range of the search value does not fully contain the range of the target value";
/// <c>gt</c> "the range above the search value intersects (i.e. overlaps) with the range of the target value";
/// <c>lt</c> "the range below the search value intersects (i.e. overlaps) with the range of the target value";
/// <c>ge</c> "the range above the search value intersects or overlaps with the range of the target value, or
/// the range of the search value fully contains the range of the target value";
/// <c>le</c> "the range below the search value intersects or overlaps with the range of the target value or
/// the range of the search value fully contains the range of the target value";
/// <c>sa</c> "the range of the search value does not overlap with the range of the target value, and the
/// range above the search value contains the range of the target value";
/// <c>eb</c> "the range of the search value does not overlap with the range of the target value, and the
/// range below the search value contains the range of the target value";
/// <c>ap</c> "the range of the search value overlaps with the range of the target value".
/// </para>
/// <para>
/// This type exists for the same reason its date sibling does, and closes the same defect one type later.
/// The table was implemented three times — once for the SQL compiler and twice for the field-level
/// expression tree — and the copies disagreed about <c>ap</c>: the field-level pair widened the window and
/// then asked for <em>containment</em>, while the compiler asked for <em>overlap</em>. Only overlap is what
/// the spec says, so <c>ap</c> was strictly under-matching wherever the field-level tree answered the query,
/// silently dropping rows whose extent straddled the edge of the tolerance window. Note that <c>eq</c> and
/// <c>ne</c> are exact complements by definition; if a change here ever makes a row satisfy both, the change
/// is wrong.
/// </para>
/// <para>
/// Note that the table decides both the operator AND which bound it applies to: <c>gt</c> and <c>sa</c> emit
/// the same operator against DIFFERENT bounds (as do <c>lt</c> and <c>eb</c>), so they cannot share an arm.
/// Collapsing them silently implements <c>sa</c>/<c>eb</c> for both, which agrees with the spec only where
/// Low = High — that is, on every point-valued row, which is why such a bug survives most test corpora.
/// Ordering comparators do not widen at all; the spec treats them as having arbitrarily high precision.
/// </para>
/// <para>
/// The 10% <c>ap</c> tolerance is a spec recommendation rather than a requirement ("systems may choose other
/// values where appropriate"), but it is floored at the search value's own precision modifier so that
/// <c>ap0</c> does not collapse to exact equality, exactly as the date path floors at the value's own width.
/// </para>
/// </remarks>
public static class NumericRangeComparisonSemantics
{
    private const decimal ApproximateMultiplier = .1M;

    /// <summary>
    /// Describes the comparison <paramref name="comparator"/> performs against an indexed numeric range.
    /// </summary>
    /// <param name="comparator">The search prefix.</param>
    /// <param name="value">
    /// The search value, whose own range — the value widened by its precision modifier, and for <c>ap</c> by
    /// the approximation tolerance — the prefix is defined against.
    /// </param>
    /// <returns>The comparison, for a backend to render.</returns>
    public static NumericRangePredicate Build(SearchComparator comparator, decimal value)
    {
        switch (comparator)
        {
            case SearchComparator.Eq:
                return Contains(value, value.GetPrescisionModifier());
            case SearchComparator.Ne:
                return DoesNotContain(value, value.GetPrescisionModifier());
            case SearchComparator.Gt:
                return Compare(NumericRangeBound.High, BinaryOperator.GreaterThan, value);
            case SearchComparator.Ge:
                return Compare(NumericRangeBound.High, BinaryOperator.GreaterThanOrEqual, value);
            case SearchComparator.Lt:
                return Compare(NumericRangeBound.Low, BinaryOperator.LessThan, value);
            case SearchComparator.Le:
                return Compare(NumericRangeBound.Low, BinaryOperator.LessThanOrEqual, value);
            case SearchComparator.Sa:
                return Compare(NumericRangeBound.Low, BinaryOperator.GreaterThan, value);
            case SearchComparator.Eb:
                return Compare(NumericRangeBound.High, BinaryOperator.LessThan, value);
            case SearchComparator.Ap:
                return Overlaps(value, Tolerance(value));
            default:
                throw new NotSupportedException($"Unknown SearchComparator '{comparator}'.");
        }
    }

    /// <summary>
    /// The half-width of the window <c>ap</c> compares against: <c>max(precision_modifier, |value| × 0.10)</c>.
    /// </summary>
    /// <param name="value">The search value.</param>
    /// <returns>The tolerance to widen the search value by.</returns>
    /// <remarks>
    /// Flooring at the precision modifier stops <c>ap0</c> collapsing to exact equality, since the
    /// proportional term is zero at zero.
    /// </remarks>
    private static decimal Tolerance(decimal value)
        => Math.Max(value.GetPrescisionModifier(), Math.Abs(value) * ApproximateMultiplier);

    private static NumericRangePredicate Compare(NumericRangeBound bound, BinaryOperator op, decimal value)
        => new NumericRangePredicate.Compare(bound, op, value);

    /// <summary>
    /// <c>eq</c>: the widened window [value - tolerance, value + tolerance] must contain [Low, High].
    /// </summary>
    /// <remarks>
    /// An unrepresentable window edge is dropped rather than computed — the arithmetic would throw
    /// <see cref="OverflowException"/> on extreme input. Dropping is exact, since no stored value lies beyond
    /// decimal range and so the dropped conjunct would have been satisfied by every row; and only one edge
    /// can ever be unrepresentable, so the two guards cannot both fire.
    /// </remarks>
    private static NumericRangePredicate Contains(decimal value, decimal tolerance)
    {
        if (!HasLowerBound(value, tolerance))
        {
            return Compare(NumericRangeBound.High, BinaryOperator.LessThanOrEqual, value + tolerance);
        }

        if (!HasUpperBound(value, tolerance))
        {
            return Compare(NumericRangeBound.Low, BinaryOperator.GreaterThanOrEqual, value - tolerance);
        }

        return new NumericRangePredicate.All(
            Compare(NumericRangeBound.Low, BinaryOperator.GreaterThanOrEqual, value - tolerance),
            Compare(NumericRangeBound.High, BinaryOperator.LessThanOrEqual, value + tolerance));
    }

    /// <summary>
    /// <c>ne</c>: the exact negation of <see cref="Contains"/>, built by De Morgan so the two partition every
    /// row. The edge guards apply negated — a disjunct no row satisfies is dropped from the Any.
    /// </summary>
    private static NumericRangePredicate DoesNotContain(decimal value, decimal tolerance)
    {
        if (!HasLowerBound(value, tolerance))
        {
            return Compare(NumericRangeBound.High, BinaryOperator.GreaterThan, value + tolerance);
        }

        if (!HasUpperBound(value, tolerance))
        {
            return Compare(NumericRangeBound.Low, BinaryOperator.LessThan, value - tolerance);
        }

        return new NumericRangePredicate.Any(
            Compare(NumericRangeBound.Low, BinaryOperator.LessThan, value - tolerance),
            Compare(NumericRangeBound.High, BinaryOperator.GreaterThan, value + tolerance));
    }

    /// <summary>
    /// <c>ap</c>: the widened window [value - tolerance, value + tolerance] must overlap [Low, High], which
    /// holds when Low is at or below the window's top and High is at or above its bottom.
    /// </summary>
    /// <remarks>
    /// The edge guards work as they do for <see cref="Contains"/>: an unrepresentable edge makes its conjunct
    /// true for every stored row, so it is dropped rather than computed.
    /// </remarks>
    private static NumericRangePredicate Overlaps(decimal value, decimal tolerance)
    {
        if (!HasLowerBound(value, tolerance))
        {
            return Compare(NumericRangeBound.Low, BinaryOperator.LessThanOrEqual, value + tolerance);
        }

        if (!HasUpperBound(value, tolerance))
        {
            return Compare(NumericRangeBound.High, BinaryOperator.GreaterThanOrEqual, value - tolerance);
        }

        return new NumericRangePredicate.All(
            Compare(NumericRangeBound.Low, BinaryOperator.LessThanOrEqual, value + tolerance),
            Compare(NumericRangeBound.High, BinaryOperator.GreaterThanOrEqual, value - tolerance));
    }

    private static bool HasLowerBound(decimal value, decimal tolerance) => value >= decimal.MinValue + tolerance;

    private static bool HasUpperBound(decimal value, decimal tolerance) => value <= decimal.MaxValue - tolerance;
}
