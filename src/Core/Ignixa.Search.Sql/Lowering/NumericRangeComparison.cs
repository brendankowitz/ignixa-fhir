using Ignixa.Search.Extensions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Builds the comparator-dependent predicate shared by Number and Quantity leaf lowering (both store
/// LowValue/HighValue), applying the FHIR search prefix table's relation between the parameter range
/// [S, E] and the resource range [Low, High] — see <see cref="DateTimeRangeComparison"/> for the same over
/// dates. eq is containment (not overlap), ne its negation, ap overlap; ordering comparators name the far bound.
/// </summary>
internal static class NumericRangeComparison
{
    public static Predicate Build(LeafContext context, SqlColumnRef lowColumn, SqlColumnRef highColumn, SearchComparator comparator, decimal value) => comparator switch
    {
        SearchComparator.Eq => BuildEq(context, lowColumn, highColumn, value),
        SearchComparator.Ne => BuildNe(context, lowColumn, highColumn, value),
        SearchComparator.Gt => new Predicate.GreaterThan(highColumn, context.Parameter(value)),
        SearchComparator.Ge => new Predicate.GreaterThanOrEqual(highColumn, context.Parameter(value)),
        SearchComparator.Lt => new Predicate.LessThan(lowColumn, context.Parameter(value)),
        SearchComparator.Le => new Predicate.LessThanOrEqual(lowColumn, context.Parameter(value)),
        SearchComparator.Sa => new Predicate.GreaterThan(lowColumn, context.Parameter(value)),
        SearchComparator.Eb => new Predicate.LessThan(highColumn, context.Parameter(value)),
        SearchComparator.Ap => BuildApproximate(context, lowColumn, highColumn, value),
        _ => throw new NotSupportedException($"Unknown SearchComparator '{comparator}'."),
    };

    /// <summary>
    /// <c>eq</c> is containment: the widened window [value - modifier, value + modifier] must contain
    /// [LowValue, HighValue]. An unrepresentable window edge is dropped, not computed (the subtraction would
    /// throw <see cref="OverflowException"/> on extreme input); dropping is exact since no stored value lies
    /// beyond decimal range, and only one edge can ever be unrepresentable, so the two guards cannot both fire.
    /// </summary>
    private static Predicate BuildEq(LeafContext context, SqlColumnRef lowColumn, SqlColumnRef highColumn, decimal value)
    {
        var modifier = value.GetPrescisionModifier();

        if (value < decimal.MinValue + modifier)
        {
            return new Predicate.LessThanOrEqual(highColumn, context.Parameter(value + modifier));
        }

        if (value > decimal.MaxValue - modifier)
        {
            return new Predicate.GreaterThanOrEqual(lowColumn, context.Parameter(value - modifier));
        }

        return new Predicate.And(
            new Predicate.GreaterThanOrEqual(lowColumn, context.Parameter(value - modifier)),
            new Predicate.LessThanOrEqual(highColumn, context.Parameter(value + modifier)));
    }

    /// <summary>
    /// <c>ne</c> is the exact negation of <see cref="BuildEq"/>'s containment, built by De Morgan so the two
    /// partition every row. The edge-representability guards apply negated: a disjunct no row satisfies is
    /// dropped from the Or.
    /// </summary>
    private static Predicate BuildNe(LeafContext context, SqlColumnRef lowColumn, SqlColumnRef highColumn, decimal value)
    {
        var modifier = value.GetPrescisionModifier();

        if (value < decimal.MinValue + modifier)
        {
            return new Predicate.GreaterThan(highColumn, context.Parameter(value + modifier));
        }

        if (value > decimal.MaxValue - modifier)
        {
            return new Predicate.LessThan(lowColumn, context.Parameter(value - modifier));
        }

        return new Predicate.Or(
            new Predicate.LessThan(lowColumn, context.Parameter(value - modifier)),
            new Predicate.GreaterThan(highColumn, context.Parameter(value + modifier)));
    }

    private static Predicate BuildApproximate(
        LeafContext context,
        SqlColumnRef lowColumn,
        SqlColumnRef highColumn,
        decimal value)
    {
        var tolerance = Math.Max(value.GetPrescisionModifier(), Math.Abs(value) * 0.10m);
        var hasLowerBound = value >= decimal.MinValue + tolerance;
        var hasUpperBound = value <= decimal.MaxValue - tolerance;

        if (!hasLowerBound)
        {
            return new Predicate.LessThanOrEqual(lowColumn, context.Parameter(value + tolerance));
        }

        if (!hasUpperBound)
        {
            return new Predicate.GreaterThanOrEqual(highColumn, context.Parameter(value - tolerance));
        }

        return new Predicate.And(
            new Predicate.LessThanOrEqual(lowColumn, context.Parameter(value + tolerance)),
            new Predicate.GreaterThanOrEqual(highColumn, context.Parameter(value - tolerance)));
    }
}
