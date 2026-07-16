using Ignixa.Search.Sql.Ast;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Builds the comparator-dependent predicate shared by Number and Quantity leaf lowering (both store
/// LowValue/HighValue with identical range semantics). Transcribed from
/// SearchParameterQueryGenerator.cs's GenerateNumberQuery/GenerateQuantityQueryAsync -- the real,
/// already-shipped SQL these comparators emit today. Ap throws: it requires a tolerance/widening input
/// this pure function doesn't have.
/// </summary>
internal static class NumericRangeComparison
{
    public static Predicate Build(SqlColumnRef lowColumn, SqlColumnRef highColumn, SearchComparator comparator, SqlParameterRef value) => comparator switch
    {
        SearchComparator.Eq => new Predicate.And(new Predicate.LessThanOrEqual(lowColumn, value), new Predicate.GreaterThanOrEqual(highColumn, value)),
        SearchComparator.Ne => new Predicate.Or(new Predicate.LessThan(highColumn, value), new Predicate.GreaterThan(lowColumn, value)),
        SearchComparator.Ge => new Predicate.GreaterThanOrEqual(lowColumn, value),
        SearchComparator.Gt or SearchComparator.Sa => new Predicate.GreaterThan(lowColumn, value),
        SearchComparator.Le => new Predicate.LessThanOrEqual(highColumn, value),
        SearchComparator.Lt or SearchComparator.Eb => new Predicate.LessThan(highColumn, value),
        SearchComparator.Ap => throw new NotSupportedException(
            "The :ap (approximately) comparator requires a tolerance/widening input this pure lowering " +
            "function doesn't have -- not implemented. Would need Lower.Run to accept an explicit widening policy."),
        _ => throw new NotSupportedException($"Unknown SearchComparator '{comparator}'."),
    };
}
