using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering.Leaf;

/// <summary>
/// Lowers a Quantity search value to a ParamSource over QuantitySearchParam — value comparison with optional
/// system (SystemId) and code (QuantityCodeId) identity constraints. A known-miss for either yields
/// <see cref="Predicate.False"/>; an absent (empty/null) system or code means no constraint and emits no
/// IS NULL guard.
/// </summary>
internal static class QuantityLoweringRule
{
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, QuantitySearchValue value, LeafContext context, short? resourceTypeId)
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
