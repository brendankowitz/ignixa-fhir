using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a TokenQuantity composite to a single ParamSource over TokenQuantityCompositeSearchParam --
/// components[0] is the token slot (Code1), components[1] is the quantity slot (LowValue2/HighValue2,
/// value comparison only -- SystemId2/QuantityCodeId2 need SystemId/QuantityCodeId resolution, the same
/// gap QuantityLoweringRule already defers). LowValue2/HighValue2 are nullable in this composite table
/// (unlike the base QuantitySearchParam's NOT NULL columns), which needs no special handling here:
/// TokenQuantityCompositeRowGenerator always populates both columns for every row it writes (mirroring
/// the base table's write-time invariant), so a NULL-Low/High row is not a real case this rule needs
/// to account for.
/// </summary>
public static class TokenQuantityLoweringRule
{
    public static CteDefinition.ParamSource Lower(
        SearchParameterInfo compositeParameter,
        IReadOnlyList<SearchParameterPredicateExpression> components,
        LeafContext context)
    {
        var table = SqlCatalog.Default.Table("TokenQuantityCompositeSearchParam");

        var tokenPredicate = TokenColumnEquality.Build(table, "Code1", (TokenSearchValue)components[0].Value, context);
        var quantityPredicate = QuantityRangePredicate(table, components[1], context);

        var predicate = new Predicate.And(tokenPredicate, quantityPredicate);
        return new CteDefinition.ParamSource(table, context.SearchParamId(compositeParameter), predicate);
    }

    private static Predicate QuantityRangePredicate(TableDescriptor table, SearchParameterPredicateExpression component, LeafContext context)
    {
        var value = (QuantitySearchValue)component.Value;
        if (!string.IsNullOrEmpty(value.System) || !string.IsNullOrEmpty(value.Code))
        {
            throw new NotSupportedException(
                "Quantity search with System or Code is not supported yet -- this rule only implements the value comparison. " +
                "SystemId/QuantityCodeId resolution needs a new ISymbolResolver method, not built yet.");
        }

        var comparisonValue = value.Low ?? value.High
            ?? throw new NotSupportedException("QuantitySearchValue has neither Low nor High set.");
        var lowColumn = new SqlColumnRef(table.TableName, "LowValue2");
        var highColumn = new SqlColumnRef(table.TableName, "HighValue2");
        return NumericRangeComparison.Build(context, lowColumn, highColumn, component.Comparator, comparisonValue);
    }
}
