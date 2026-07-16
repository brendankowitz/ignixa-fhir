using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a DateTime search value to a ParamSource over DateTimeSearchParam -- range-overlap semantics
/// between the stored row's [StartDateTime, EndDateTime] and the search value's own [Start, End]
/// (which already encodes precision -- a year-only search is a full-year range by the time it reaches
/// here). Transcribed from SearchValueExpressionBuilderHelper.Visit(DateTimeSearchValue), the real,
/// live-executed DateTime comparator branch (a separate method from the Number/Quantity
/// GenerateNumberExpression path -- no precision-widening arithmetic applies here since precision is
/// already resolved into Start/End before this rule runs). :ap throws -- it requires
/// DateTimeOffset.UtcNow at lowering time, which this pure function doesn't have.
/// </summary>
public static class DateTimeLoweringRule
{
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, DateTimeSearchValue value, LeafContext context)
    {
        var table = SqlCatalog.Default.Table("DateTimeSearchParam");
        var startColumn = new SqlColumnRef(table.TableName, "StartDateTime");
        var endColumn = new SqlColumnRef(table.TableName, "EndDateTime");

        Predicate predicateExpr = predicate.Comparator switch
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
            _ => throw new NotSupportedException($"Unknown SearchComparator '{predicate.Comparator}'."),
        };

        return new CteDefinition.ParamSource(table, context.SearchParamId(predicate.Parameter), predicateExpr);
    }
}
