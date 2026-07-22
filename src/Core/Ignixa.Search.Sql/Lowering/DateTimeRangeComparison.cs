using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Builds the comparator-dependent predicate shared by base and composite DateTime lowering — both store
/// [StartDateTime, EndDateTime] and compare, with range-overlap semantics, against the search value's own
/// [Start, End] (which already encodes FHIR partial-date precision). :ap widens [Start, End] by
/// <see cref="ApproximateDateRange.Widen"/> using <see cref="LeafContext.ApproximationReferenceTime"/>,
/// then reuses the same overlap shape as Eq against the widened bounds.
/// </summary>
internal static class DateTimeRangeComparison
{
    public static Predicate Build(LeafContext context, SqlColumnRef startColumn, SqlColumnRef endColumn, SearchComparator comparator, DateTimeSearchValue value) => comparator switch
    {
        SearchComparator.Eq => new Predicate.And(
            new Predicate.LessThanOrEqual(startColumn, context.Parameter(value.End)),
            new Predicate.GreaterThanOrEqual(endColumn, context.Parameter(value.Start))),
        SearchComparator.Ne => new Predicate.Or(
            new Predicate.LessThan(startColumn, context.Parameter(value.Start)),
            new Predicate.GreaterThan(endColumn, context.Parameter(value.End))),
        SearchComparator.Lt => new Predicate.LessThan(startColumn, context.Parameter(value.Start)),
        SearchComparator.Gt => new Predicate.GreaterThan(endColumn, context.Parameter(value.End)),
        SearchComparator.Le => new Predicate.LessThanOrEqual(startColumn, context.Parameter(value.End)),
        SearchComparator.Ge => new Predicate.GreaterThanOrEqual(endColumn, context.Parameter(value.Start)),
        SearchComparator.Sa => new Predicate.GreaterThan(startColumn, context.Parameter(value.End)),
        SearchComparator.Eb => new Predicate.LessThan(endColumn, context.Parameter(value.Start)),
        SearchComparator.Ap => BuildApproximate(context, startColumn, endColumn, value),
        _ => throw new NotSupportedException($"Unknown SearchComparator '{comparator}'."),
    };

    private static Predicate BuildApproximate(LeafContext context, SqlColumnRef startColumn, SqlColumnRef endColumn, DateTimeSearchValue value)
    {
        var (widenedStart, widenedEnd) = ApproximateDateRange.Widen(value, context.ApproximationReferenceTime);
        return new Predicate.And(
            new Predicate.LessThanOrEqual(startColumn, context.Parameter(widenedEnd)),
            new Predicate.GreaterThanOrEqual(endColumn, context.Parameter(widenedStart)));
    }
}
