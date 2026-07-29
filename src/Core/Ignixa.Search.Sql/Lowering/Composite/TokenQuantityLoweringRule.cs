using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering.Composite;

/// <summary>
/// Lowers a TokenQuantity composite to a single ParamSource over TokenQuantityCompositeSearchParam —
/// components[0] is the token slot (SystemId1/Code1), components[1] is the quantity slot
/// (LowValue2/HighValue2/SystemId2/QuantityCodeId2). System and code identity constraints on the
/// quantity slot are delegated to <see cref="QuantityColumnPredicate"/>; a known-miss for either
/// produces <see cref="Predicate.False"/> for the quantity slot.
/// </summary>
internal static class TokenQuantityLoweringRule
{
    public static CteDefinition.ParamSource Lower(
        SearchParameterInfo compositeParameter,
        IReadOnlyList<SearchParameterPredicateExpression> components,
        LeafContext context,
        short? resourceTypeId)
    {
        var table = SqlCatalog.Default.Table("TokenQuantityCompositeSearchParam");

        var tokenPredicate = TokenColumnEquality.Build(table, "SystemId1", "Code1", "CodeOverflow1", (TokenSearchValue)components[0].Value, context);
        var quantityPredicate = QuantitySlotPredicate(table, components[1], context);

        var predicate = new Predicate.And(tokenPredicate, quantityPredicate);
        return new CteDefinition.ParamSource(table, resourceTypeId, context.SearchParamId(compositeParameter), predicate);
    }

    private static Predicate QuantitySlotPredicate(TableDescriptor table, SearchParameterPredicateExpression component, LeafContext context)
        => QuantityColumnPredicate.Build(
            table,
            lowColumn: "LowValue2",
            highColumn: "HighValue2",
            systemColumn: "SystemId2",
            codeColumn: "QuantityCodeId2",
            component.Comparator,
            (QuantitySearchValue)component.Value,
            context);
}
