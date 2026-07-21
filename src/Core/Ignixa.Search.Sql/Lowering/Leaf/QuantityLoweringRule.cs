using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering.Leaf;

/// <summary>
/// Lowers a Quantity search value to a ParamSource over QuantitySearchParam — value comparison only.
/// Matching System or Code needs SystemId/QuantityCodeId resolution that ISymbolResolver does not offer
/// yet, so a search that specifies either throws rather than silently ignoring the constraint.
/// </summary>
public static class QuantityLoweringRule
{
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, QuantitySearchValue value, LeafContext context, short resourceTypeId)
    {
        if (!string.IsNullOrEmpty(value.System) || !string.IsNullOrEmpty(value.Code))
        {
            throw new NotSupportedException(
                "Quantity search with System or Code is not supported yet -- this rule only implements the value comparison. " +
                "SystemId/QuantityCodeId resolution needs a new ISymbolResolver method, not built yet.");
        }

        var table = SqlCatalog.Default.Table("QuantitySearchParam");
        var lowColumn = new SqlColumnRef(table.TableName, "LowValue");
        var highColumn = new SqlColumnRef(table.TableName, "HighValue");

        var comparisonValue = value.Low ?? value.High
            ?? throw new NotSupportedException("QuantitySearchValue has neither Low nor High set.");
        var predicateExpr = NumericRangeComparison.Build(context, lowColumn, highColumn, predicate.Comparator, comparisonValue);

        return new CteDefinition.ParamSource(table, resourceTypeId, context.SearchParamId(predicate.Parameter), predicateExpr);
    }
}
