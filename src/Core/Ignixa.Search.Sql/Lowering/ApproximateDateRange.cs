using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Widens a date search value's [Start, End] interval for the :ap comparator by the FHIR-recommended 10
/// percent tolerance, measured against one caller-supplied reference instant: the distance from the
/// interval's midpoint to that instant, divided by ten. Pure -- it takes the reference time as an
/// explicit parameter and never reads the ambient clock, preserving Lower's determinism invariant.
/// </summary>
internal static class ApproximateDateRange
{
    /// <summary>
    /// Computes the widened [Start, End] endpoints for a date :ap comparison. Throws
    /// <see cref="InvalidOperationException"/> when <paramref name="referenceTime"/> is null -- a direct
    /// caller compiling a date :ap search must supply <c>Lower.Run</c>'s approximationReferenceTime
    /// parameter. Throws <see cref="ArgumentOutOfRangeException"/>, rather than clamping or letting a raw
    /// <see cref="OverflowException"/> escape, when the widened endpoint would fall outside the
    /// representable <see cref="DateTimeOffset"/> range.
    /// </summary>
    public static (DateTimeOffset Start, DateTimeOffset End) Widen(DateTimeSearchValue value, DateTimeOffset? referenceTime)
    {
        if (referenceTime is not { } reference)
        {
            throw new InvalidOperationException(
                "The date ':ap' (approximately) comparator requires an explicit reference instant -- " +
                "Lower.Run's approximationReferenceTime parameter must be supplied (non-null) to compile a " +
                "date ':ap' search. SearchCompiler.CompileAsync supplies this automatically from its " +
                "TimeProvider; a direct Lower.Run caller must pass it explicitly.");
        }

        var midpointTicks = value.Start.UtcTicks + ((value.End.UtcTicks - value.Start.UtcTicks) / 2);
        var toleranceTicks = Math.Abs(reference.UtcTicks - midpointTicks) / 10;

        return (
            Subtract(value.Start, toleranceTicks),
            Add(value.End, toleranceTicks));
    }

    private static DateTimeOffset Subtract(DateTimeOffset value, long toleranceTicks)
    {
        var minTicks = DateTimeOffset.MinValue.UtcTicks;
        if (value.UtcTicks - minTicks < toleranceTicks)
        {
            throw new ArgumentOutOfRangeException(
                nameof(toleranceTicks),
                toleranceTicks,
                $"Widening '{value:o}' downward by {toleranceTicks} ticks for ':ap' would underflow DateTimeOffset.MinValue.");
        }

        return new DateTimeOffset(value.UtcTicks - toleranceTicks, TimeSpan.Zero);
    }

    private static DateTimeOffset Add(DateTimeOffset value, long toleranceTicks)
    {
        var maxTicks = DateTimeOffset.MaxValue.UtcTicks;
        if (maxTicks - value.UtcTicks < toleranceTicks)
        {
            throw new ArgumentOutOfRangeException(
                nameof(toleranceTicks),
                toleranceTicks,
                $"Widening '{value:o}' upward by {toleranceTicks} ticks for ':ap' would overflow DateTimeOffset.MaxValue.");
        }

        return new DateTimeOffset(value.UtcTicks + toleranceTicks, TimeSpan.Zero);
    }
}
