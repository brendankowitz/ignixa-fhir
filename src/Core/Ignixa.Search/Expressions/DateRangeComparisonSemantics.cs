// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Expressions;

/// <summary>
/// The FHIR date search prefix table, stated once.
/// </summary>
/// <remarks>
/// <para>
/// Every prefix is defined by the specification (https://hl7.org/fhir/R4/search.html#prefix) as a relation
/// between two intervals — the range of the search value and the range of the target value — so that is how
/// it is written here, and backends render the result rather than re-deriving it:
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
/// <c>eb</c> "the range of the search value does overlap not with the range of the target value, and the
/// range below the search value contains the range of the target value";
/// <c>ap</c> "the range of the search value overlaps with the range of the target value".
/// </para>
/// <para>
/// This type exists because the table was previously implemented three times — once for the SQL compiler and
/// twice for the field-level expression tree — and the copies had drifted on <c>eq</c> and <c>ap</c>, in
/// opposite directions, for long enough that a test had been written pinning each deviation. Note that
/// <c>eq</c> and <c>ne</c> are exact complements by definition; if a change here ever makes a row satisfy
/// both, the change is wrong.
/// </para>
/// </remarks>
public static class DateRangeComparisonSemantics
{
    private const decimal ApproximateMultiplier = .1M;

    /// <summary>
    /// Describes the comparison <paramref name="comparator"/> performs against an indexed date range.
    /// </summary>
    /// <param name="comparator">The search prefix.</param>
    /// <param name="value">The search value, whose own [Start, End] range the prefix is defined against.</param>
    /// <param name="approximationReferenceTime">
    /// The instant <c>ap</c> measures distance from when widening its window. Required only for <c>ap</c>;
    /// ignored by every other prefix.
    /// </param>
    /// <returns>The comparison, for a backend to render.</returns>
    public static DateRangePredicate Build(
        SearchComparator comparator,
        DateTimeSearchValue value,
        DateTimeOffset? approximationReferenceTime)
    {
        EnsureArg.IsNotNull(value, nameof(value));

        switch (comparator)
        {
            case SearchComparator.Eq:
                return Contains(value.Start, value.End);
            case SearchComparator.Ne:
                return DoesNotContain(value.Start, value.End);
            case SearchComparator.Lt:
                return Compare(DateRangeBound.Start, BinaryOperator.LessThan, value.Start);
            case SearchComparator.Gt:
                return Compare(DateRangeBound.End, BinaryOperator.GreaterThan, value.End);
            case SearchComparator.Le:
                return Compare(DateRangeBound.Start, BinaryOperator.LessThanOrEqual, value.End);
            case SearchComparator.Ge:
                return Compare(DateRangeBound.End, BinaryOperator.GreaterThanOrEqual, value.Start);
            case SearchComparator.Sa:
                return Compare(DateRangeBound.Start, BinaryOperator.GreaterThan, value.End);
            case SearchComparator.Eb:
                return Compare(DateRangeBound.End, BinaryOperator.LessThan, value.Start);
            case SearchComparator.Ap:
                (DateTimeOffset widenedStart, DateTimeOffset widenedEnd) = Widen(value, approximationReferenceTime);
                return Overlaps(widenedStart, widenedEnd);
            default:
                throw new NotSupportedException($"Unknown SearchComparator '{comparator}'.");
        }
    }

    /// <summary>
    /// Widens a date value's [Start, End] for the <c>ap</c> comparator, using
    /// <c>max(precision_modifier, distance × 0.10)</c> where distance is midpoint-to-reference and the
    /// modifier is the value's own width.
    /// </summary>
    /// <param name="value">The search value to widen.</param>
    /// <param name="referenceTime">The instant distance is measured from.</param>
    /// <returns>The widened endpoints.</returns>
    /// <remarks>
    /// Flooring at the value's own width stops <c>date=ap&lt;now&gt;</c> collapsing to exact equality, since
    /// distance is zero at "now". Out-of-range endpoints saturate rather than throwing, so
    /// <c>date=ap0001-01-01</c> compiles. Pure — the reference time is a parameter, never the clock.
    /// </remarks>
    public static (DateTimeOffset Start, DateTimeOffset End) Widen(DateTimeSearchValue value, DateTimeOffset? referenceTime)
    {
        EnsureArg.IsNotNull(value, nameof(value));

        if (referenceTime is not { } reference)
        {
            throw new InvalidOperationException(
                "The date ':ap' (approximately) comparator requires an explicit reference instant, but none " +
                "was supplied. Callers must pass the instant 'approximately' is measured against rather than " +
                "reading the clock here, so that lowering the same query twice produces the same predicate.");
        }

        long precisionTicks = value.End.UtcTicks - value.Start.UtcTicks;
        long midpointTicks = value.Start.UtcTicks + precisionTicks / 2;
        long proportionalTicks = (long)(Math.Abs(reference.UtcTicks - midpointTicks) * ApproximateMultiplier);
        long toleranceTicks = Math.Max(precisionTicks, proportionalTicks);

        return (SubtractSaturating(value.Start, toleranceTicks), AddSaturating(value.End, toleranceTicks));
    }

    private static DateRangePredicate Compare(DateRangeBound bound, BinaryOperator op, DateTimeOffset value)
        => new DateRangePredicate.Compare(bound, op, value);

    private static DateRangePredicate Contains(DateTimeOffset start, DateTimeOffset end)
        => new DateRangePredicate.All(
            Compare(DateRangeBound.Start, BinaryOperator.GreaterThanOrEqual, start),
            Compare(DateRangeBound.End, BinaryOperator.LessThanOrEqual, end));

    private static DateRangePredicate DoesNotContain(DateTimeOffset start, DateTimeOffset end)
        => new DateRangePredicate.Any(
            Compare(DateRangeBound.Start, BinaryOperator.LessThan, start),
            Compare(DateRangeBound.End, BinaryOperator.GreaterThan, end));

    private static DateRangePredicate Overlaps(DateTimeOffset start, DateTimeOffset end)
        => new DateRangePredicate.All(
            Compare(DateRangeBound.Start, BinaryOperator.LessThanOrEqual, end),
            Compare(DateRangeBound.End, BinaryOperator.GreaterThanOrEqual, start));

    private static DateTimeOffset SubtractSaturating(DateTimeOffset value, long toleranceTicks)
        => value.UtcTicks - DateTimeOffset.MinValue.UtcTicks < toleranceTicks
            ? DateTimeOffset.MinValue
            : new DateTimeOffset(value.UtcTicks - toleranceTicks, TimeSpan.Zero);

    private static DateTimeOffset AddSaturating(DateTimeOffset value, long toleranceTicks)
        => DateTimeOffset.MaxValue.UtcTicks - value.UtcTicks < toleranceTicks
            ? DateTimeOffset.MaxValue
            : new DateTimeOffset(value.UtcTicks + toleranceTicks, TimeSpan.Zero);
}
