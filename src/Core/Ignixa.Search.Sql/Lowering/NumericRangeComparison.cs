using Ignixa.Search.Extensions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Builds the comparator-dependent predicate shared by Number and Quantity leaf lowering — both store
/// LowValue/HighValue with identical range semantics.
/// <para>
/// Eq/Ne/Ap widen the search value into a range by the FHIR implied-decimal-precision tolerance before
/// comparing (:ap widens by <c>max(precision_modifier, abs(value) × 0.10)</c>), then apply the same
/// relations the FHIR search prefix table (search.html) defines for every ranged type, so numbers,
/// quantities, and dates share one set of semantics. <c>eq</c> is CONTAINMENT — the parameter range fully
/// contains the resource range. <c>ne</c> is the exact negation of that containment, which makes
/// <c>eq</c> and <c>ne</c> genuine complements: every stored row satisfies exactly one of them.
/// <c>ap</c> is OVERLAP against the widened bounds, matching
/// <see cref="DateTimeRangeComparison"/>'s <c>ap</c> — the spec defines <c>ap</c> as the parameter range
/// overlapping the resource range, a deliberately looser relation than <c>eq</c>.
/// </para>
/// <para>
/// For a point-valued row (LowValue = HighValue, what a plain <c>valueQuantity</c> or number indexes to)
/// containment and overlap coincide; the distinction only bites on a row that stores a genuine range,
/// such as an indexed <c>Range</c> element.
/// </para>
/// </summary>
internal static class NumericRangeComparison
{
    public static Predicate Build(LeafContext context, SqlColumnRef lowColumn, SqlColumnRef highColumn, SearchComparator comparator, decimal value) => comparator switch
    {
        SearchComparator.Eq => BuildEq(context, lowColumn, highColumn, value),
        SearchComparator.Ne => BuildNe(context, lowColumn, highColumn, value),
        SearchComparator.Ge => new Predicate.GreaterThanOrEqual(lowColumn, context.Parameter(value)),
        SearchComparator.Gt or SearchComparator.Sa => new Predicate.GreaterThan(lowColumn, context.Parameter(value)),
        SearchComparator.Le => new Predicate.LessThanOrEqual(highColumn, context.Parameter(value)),
        SearchComparator.Lt or SearchComparator.Eb => new Predicate.LessThan(highColumn, context.Parameter(value)),
        SearchComparator.Ap => BuildApproximate(context, lowColumn, highColumn, value),
        _ => throw new NotSupportedException($"Unknown SearchComparator '{comparator}'."),
    };

    private static Predicate BuildEq(LeafContext context, SqlColumnRef lowColumn, SqlColumnRef highColumn, decimal value)
    {
        var modifier = value.GetPrescisionModifier();
        var lowerBound = context.Parameter(value - modifier);
        var upperBound = context.Parameter(value + modifier);
        return new Predicate.And(
            new Predicate.GreaterThanOrEqual(lowColumn, lowerBound),
            new Predicate.LessThanOrEqual(highColumn, upperBound));
    }

    private static Predicate BuildNe(LeafContext context, SqlColumnRef lowColumn, SqlColumnRef highColumn, decimal value)
    {
        var modifier = value.GetPrescisionModifier();
        var lowerBound = context.Parameter(value - modifier);
        var upperBound = context.Parameter(value + modifier);
        return new Predicate.Or(
            new Predicate.LessThan(lowColumn, lowerBound),
            new Predicate.GreaterThan(highColumn, upperBound));
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
