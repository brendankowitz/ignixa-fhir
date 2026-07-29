using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Widens a date search value's [Start, End] interval for the :ap comparator, mirroring the numeric
/// :ap formula in <see cref="NumericRangeComparison"/>: <c>max(precision_modifier, distance × 0.10)</c>.
/// For dates the distance is measured from the interval's midpoint to one caller-supplied reference
/// instant, and the precision modifier is the value's own [Start, End] width -- a partial date such as
/// <c>2010-01</c> spans its whole month, while a full instant spans zero. Pure -- it takes the reference
/// time as an explicit parameter and never reads the ambient clock, preserving Lower's determinism
/// invariant.
/// </summary>
/// <remarks>
/// The spec's 10 percent figure is a recommendation ("systems may choose other values where
/// appropriate"). Flooring at the value's own precision is a deliberate deviation: without it
/// <c>date=ap&lt;today&gt;</c> degenerates to exact equality, because the midpoint-to-reference distance
/// -- and therefore the tolerance -- is zero at "now". That is the single most likely real-world :ap
/// query, so it is also the one that must not collapse.
/// </remarks>
internal static class ApproximateDateRange
{
    /// <summary>
    /// Computes the widened [Start, End] endpoints for a date :ap comparison. Throws
    /// <see cref="InvalidOperationException"/> when <paramref name="referenceTime"/> is null, which
    /// <see cref="SearchSqlCompiler"/> never allows -- it always supplies the instant from its
    /// <see cref="TimeProvider"/>. Endpoints that would fall outside the representable
    /// <see cref="DateTimeOffset"/> range saturate at <see cref="DateTimeOffset.MinValue"/>/
    /// <see cref="DateTimeOffset.MaxValue"/>, matching how numeric :ap saturates at the decimal bounds.
    /// <c>date=ap0001-01-01</c> is legal user input and must compile, not throw.
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
