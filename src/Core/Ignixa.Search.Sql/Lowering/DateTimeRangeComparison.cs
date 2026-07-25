using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Builds the comparator-dependent predicate shared by base and composite DateTime lowering — both store
/// [StartDateTime, EndDateTime] and compare against the search value's own [Start, End], which already
/// encodes FHIR partial-date precision.
/// <para>
/// The comparators implement the FHIR search prefix table (search.html), which defines every prefix as a
/// relation between the parameter's range and the resource's range. <c>eq</c> is CONTAINMENT — the
/// parameter range fully contains the resource range — so <c>date=2013</c> matches a resource dated
/// <c>2013-01-14</c> but <c>date=2013-01-14</c> does not match one dated <c>2013</c>. <c>ne</c> is the
/// exact negation of that containment, which makes <c>eq</c> and <c>ne</c> genuine complements: every
/// stored row satisfies exactly one of them. <c>ap</c> is OVERLAP, not containment, against the bounds
/// widened by <see cref="ApproximateDateRange.Widen"/> using
/// <see cref="LeafContext.ApproximationReferenceTime"/> — the spec defines <c>ap</c> as the parameter
/// range overlapping the resource range, and a looser relation than <c>eq</c> is the point of it.
/// </para>
/// </summary>
internal static class DateTimeRangeComparison
{
    public static Predicate Build(LeafContext context, SqlColumnRef startColumn, SqlColumnRef endColumn, SearchComparator comparator, DateTimeSearchValue value) => comparator switch
    {
        SearchComparator.Eq => new Predicate.And(
            new Predicate.GreaterThanOrEqual(startColumn, context.Parameter(value.Start)),
            new Predicate.LessThanOrEqual(endColumn, context.Parameter(value.End))),
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
