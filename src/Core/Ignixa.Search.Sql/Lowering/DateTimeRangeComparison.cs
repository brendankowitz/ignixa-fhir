using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Builds the comparator-dependent predicate shared by DateTime leaf lowering (base and composite --
/// both store [StartDateTime, EndDateTime] with identical range-overlap semantics against the search
/// value's own [Start, End], which already encodes FHIR partial-date precision by construction).
/// Transcribed once from SearchValueExpressionBuilderHelper.Visit(DateTimeSearchValue), the real,
/// live-executed comparator branch. :ap throws -- it requires DateTimeOffset.UtcNow at lowering time,
/// which this pure function doesn't have.
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
        SearchComparator.Ap => throw new NotSupportedException(
            "The :ap (approximately) comparator requires DateTimeOffset.UtcNow at lowering time, which " +
            "conflicts with Lower's purity invariant -- not implemented. Would need Lower.Run to accept an explicit 'now' parameter."),
        _ => throw new NotSupportedException($"Unknown SearchComparator '{comparator}'."),
    };
}
