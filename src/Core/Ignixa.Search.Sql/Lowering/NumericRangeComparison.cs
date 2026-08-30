using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Renders the comparator-dependent predicate shared by Number and Quantity leaf lowering (both store
/// LowValue/HighValue) from <see cref="NumericRangeComparisonSemantics"/>, which states the FHIR search
/// prefix table once for every backend — see <see cref="DateTimeRangeComparison"/> for the same over dates.
/// </summary>
/// <remarks>
/// The table is deliberately not restated here. It used to be, alongside two more copies behind the
/// field-level expression tree, and the copies had drifted on <c>ap</c>: this engine had the spec's overlap,
/// the field-level pair had a containment. This type now decides only how a comparison is spelled in SQL,
/// never what it means.
/// </remarks>
internal static class NumericRangeComparison
{
    public static Predicate Build(
        LeafContext context,
        SqlColumnRef lowColumn,
        SqlColumnRef highColumn,
        SearchComparator comparator,
        decimal value)
        => Render(NumericRangeComparisonSemantics.Build(comparator, value), context, lowColumn, highColumn);

    private static Predicate Render(
        NumericRangePredicate predicate,
        LeafContext context,
        SqlColumnRef lowColumn,
        SqlColumnRef highColumn)
    {
        switch (predicate)
        {
            case NumericRangePredicate.All all:
                return new Predicate.And(
                    Render(all.Left, context, lowColumn, highColumn),
                    Render(all.Right, context, lowColumn, highColumn));
            case NumericRangePredicate.Any any:
                return new Predicate.Or(
                    Render(any.Left, context, lowColumn, highColumn),
                    Render(any.Right, context, lowColumn, highColumn));
            case NumericRangePredicate.Compare compare:
                return Compare(
                    compare,
                    compare.Bound == NumericRangeBound.Low ? lowColumn : highColumn,
                    context.Parameter(compare.Value));
            default:
                throw new NotSupportedException($"Unknown {nameof(NumericRangePredicate)} '{predicate?.GetType().Name}'.");
        }
    }

    private static Predicate Compare(NumericRangePredicate.Compare compare, SqlColumnRef column, SqlParameterRef value)
        => compare.Operator switch
        {
            BinaryOperator.LessThan => new Predicate.LessThan(column, value),
            BinaryOperator.LessThanOrEqual => new Predicate.LessThanOrEqual(column, value),
            BinaryOperator.GreaterThan => new Predicate.GreaterThan(column, value),
            BinaryOperator.GreaterThanOrEqual => new Predicate.GreaterThanOrEqual(column, value),
            _ => throw new NotSupportedException($"Numeric lowering cannot render operator '{compare.Operator}'."),
        };
}
