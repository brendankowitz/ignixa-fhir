using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Widens a date value's [Start, End] for the :ap comparator, mirroring the numeric formula in
/// <see cref="NumericRangeComparison"/>: <c>max(precision_modifier, distance × 0.10)</c> (distance is
/// midpoint-to-reference, modifier the value's own width). Flooring at that width stops <c>date=ap&lt;now&gt;</c>
/// collapsing to exact equality (distance is zero at "now"). Pure — reference time is a parameter, not the clock.
/// </summary>
internal static class ApproximateDateRange
{
    /// <summary>
    /// Computes the widened [Start, End] endpoints for a date :ap comparison. Throws when
    /// <paramref name="referenceTime"/> is null (which <see cref="SearchSqlCompiler"/> never allows).
    /// Out-of-range endpoints saturate at <see cref="DateTimeOffset.MinValue"/>/<see cref="DateTimeOffset.MaxValue"/>,
    /// like numeric :ap, so <c>date=ap0001-01-01</c> compiles rather than throwing.
    /// </summary>
    public static (DateTimeOffset Start, DateTimeOffset End) Widen(DateTimeSearchValue value, DateTimeOffset? referenceTime)
    {
        if (referenceTime is not { } reference)
        {
            throw new InvalidOperationException(
                "The date ':ap' (approximately) comparator requires an explicit reference instant, but the " +
                "lowering context was constructed without one. SearchSqlCompiler supplies that instant from " +
                "its TimeProvider on every path, so reaching this state means the compiler was bypassed.");
        }

        var precisionTicks = value.End.UtcTicks - value.Start.UtcTicks;
        var midpointTicks = value.Start.UtcTicks + (precisionTicks / 2);
        var proportionalTicks = Math.Abs(reference.UtcTicks - midpointTicks) / 10;
        var toleranceTicks = Math.Max(precisionTicks, proportionalTicks);

        return (
            SubtractSaturating(value.Start, toleranceTicks),
            AddSaturating(value.End, toleranceTicks));
    }

    private static DateTimeOffset SubtractSaturating(DateTimeOffset value, long toleranceTicks)
        => value.UtcTicks - DateTimeOffset.MinValue.UtcTicks < toleranceTicks
            ? DateTimeOffset.MinValue
            : new DateTimeOffset(value.UtcTicks - toleranceTicks, TimeSpan.Zero);

    private static DateTimeOffset AddSaturating(DateTimeOffset value, long toleranceTicks)
        => DateTimeOffset.MaxValue.UtcTicks - value.UtcTicks < toleranceTicks
            ? DateTimeOffset.MaxValue
            : new DateTimeOffset(value.UtcTicks + toleranceTicks, TimeSpan.Zero);
}
