using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a Quantity search value to a ParamSource over QuantitySearchParam -- value comparison only.
/// System/Code matching needs SystemId/QuantityCodeId resolution, a genuinely separate resolver
/// mechanism (SearchIndexReferenceDataCache.GetOrCreateSystemIdAsync/GetOrCreateQuantityCodeIdAsync in
/// the DataLayer today, no ISymbolResolver equivalent yet) -- not implemented; throws rather than
/// silently ignoring a system/code constraint the user actually specified.
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
