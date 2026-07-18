using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering.Leaf;

/// <summary>
/// Lowers a DateTime search value to a ParamSource over DateTimeSearchParam, via DateTimeRangeComparison.
/// </summary>
public static class DateTimeLoweringRule
{
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, DateTimeSearchValue value, LeafContext context, short resourceTypeId)
    {
        var table = SqlCatalog.Default.Table("DateTimeSearchParam");
        var startColumn = new SqlColumnRef(table.TableName, "StartDateTime");
        var endColumn = new SqlColumnRef(table.TableName, "EndDateTime");
        var predicateExpr = DateTimeRangeComparison.Build(context, startColumn, endColumn, predicate.Comparator, value);

        return new CteDefinition.ParamSource(table, resourceTypeId, context.SearchParamId(predicate.Parameter), predicateExpr);
    }
}
