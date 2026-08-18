using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering.Leaf;

/// <summary>
/// Lowers a Number search value to a ParamSource over NumberSearchParam. Every comparator, :ap included, is
/// supported via <see cref="NumericRangeComparison"/>.
/// </summary>
internal static class NumberLoweringRule
{
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, NumberSearchValue value, LeafContext context, short? resourceTypeId)
    {
        var table = SqlCatalog.Default.Table("NumberSearchParam");
        var lowColumn = new SqlColumnRef(table.TableName, "LowValue");
        var highColumn = new SqlColumnRef(table.TableName, "HighValue");

        var comparisonValue = value.Low ?? value.High
            ?? throw new NotSupportedException("NumberSearchValue has neither Low nor High set.");
        var predicateExpr = NumericRangeComparison.Build(context, lowColumn, highColumn, predicate.Comparator, comparisonValue);

        return new CteDefinition.ParamSource(table, resourceTypeId, context.SearchParamId(predicate.Parameter), predicateExpr);
    }
}
