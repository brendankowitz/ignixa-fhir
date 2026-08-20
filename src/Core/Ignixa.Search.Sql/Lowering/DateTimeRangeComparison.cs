using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Renders the comparator-dependent predicate shared by base and composite DateTime lowering (both store
/// [StartDateTime, EndDateTime]) from <see cref="DateRangeComparisonSemantics"/>, which states the FHIR
/// search prefix table once for every backend.
/// </summary>
/// <remarks>
/// The table is deliberately not restated here. It used to be, alongside two more copies behind the
/// field-level expression tree, and the copies had drifted on <c>eq</c> and <c>ap</c> in opposite
/// directions. This type now decides only how a comparison is spelled in SQL, never what it means.
/// </remarks>
internal static class DateTimeRangeComparison
{
    public static Predicate Build(
        LeafContext context,
        SqlColumnRef startColumn,
        SqlColumnRef endColumn,
        SearchComparator comparator,
        DateTimeSearchValue value)
        => Render(
            DateRangeComparisonSemantics.Build(comparator, value, ApproximationReference(comparator, context)),
            context,
            startColumn,
            endColumn);

    // Resolved here rather than inside the shared semantics so that a missing reference instant is reported
    // as this compiler's precondition failure, naming the component that should have supplied it.
    private static DateTimeOffset? ApproximationReference(SearchComparator comparator, LeafContext context)
        => comparator == SearchComparator.Ap
            ? ApproximateDateRange.RequireReferenceTime(context.ApproximationReferenceTime)
            : null;

    private static Predicate Render(
        DateRangePredicate predicate,
        LeafContext context,
        SqlColumnRef startColumn,
        SqlColumnRef endColumn)
    {
        switch (predicate)
        {
            case DateRangePredicate.All all:
                return new Predicate.And(
                    Render(all.Left, context, startColumn, endColumn),
                    Render(all.Right, context, startColumn, endColumn));
            case DateRangePredicate.Any any:
                return new Predicate.Or(
                    Render(any.Left, context, startColumn, endColumn),
                    Render(any.Right, context, startColumn, endColumn));
            case DateRangePredicate.Compare compare:
                return Compare(
                    compare,
                    compare.Bound == DateRangeBound.Start ? startColumn : endColumn,
                    context.Parameter(compare.Value));
            default:
                throw new NotSupportedException($"Unknown {nameof(DateRangePredicate)} '{predicate?.GetType().Name}'.");
        }
    }

    private static Predicate Compare(DateRangePredicate.Compare compare, SqlColumnRef column, SqlParameterRef value)
        => compare.Operator switch
        {
            BinaryOperator.LessThan => new Predicate.LessThan(column, value),
            BinaryOperator.LessThanOrEqual => new Predicate.LessThanOrEqual(column, value),
            BinaryOperator.GreaterThan => new Predicate.GreaterThan(column, value),
            BinaryOperator.GreaterThanOrEqual => new Predicate.GreaterThanOrEqual(column, value),
            _ => throw new NotSupportedException($"Date lowering cannot render operator '{compare.Operator}'."),
        };
}
