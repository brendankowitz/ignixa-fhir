using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a Number search value to a ParamSource over NumberSearchParam. All comparators except :ap
/// (see NumericRangeComparison) are supported, matching the real SQL SearchParameterQueryGenerator
/// already emits for this table.
/// </summary>
public static class NumberLoweringRule
{
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, NumberSearchValue value, LeafContext context)
    {
        var table = SqlCatalog.Default.Table("NumberSearchParam");
        var lowColumn = new SqlColumnRef(table.TableName, "LowValue");
        var highColumn = new SqlColumnRef(table.TableName, "HighValue");

        var comparisonValue = value.Low ?? value.High
            ?? throw new NotSupportedException("NumberSearchValue has neither Low nor High set.");
        var predicateExpr = NumericRangeComparison.Build(lowColumn, highColumn, predicate.Comparator, context.Parameter(comparisonValue));

        return new CteDefinition.ParamSource(table, context.SearchParamId(predicate.Parameter), predicateExpr);
    }
}
