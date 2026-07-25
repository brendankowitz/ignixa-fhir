using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering.Leaf;

/// <summary>
/// Lowers a Quantity search value to a ParamSource over QuantitySearchParam — value comparison with
/// optional system and code identity constraints. Non-empty system resolves to SystemId; non-empty
/// code resolves to QuantityCodeId. A known-miss for either produces <see cref="Predicate.False"/>.
/// An absent (empty or null) system or code means no constraint — no IS NULL guard is emitted.
/// </summary>
public static class QuantityLoweringRule
{
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, QuantitySearchValue value, LeafContext context, short resourceTypeId)
    {
        var table = SqlCatalog.Default.Table("QuantitySearchParam");
        var predicateExpr = QuantityColumnPredicate.Build(
            table,
            lowColumn: "LowValue",
            highColumn: "HighValue",
            systemColumn: "SystemId",
            codeColumn: "QuantityCodeId",
            predicate.Comparator,
            value,
            context);

        return new CteDefinition.ParamSource(table, resourceTypeId, context.SearchParamId(predicate.Parameter), predicateExpr);
    }
}
