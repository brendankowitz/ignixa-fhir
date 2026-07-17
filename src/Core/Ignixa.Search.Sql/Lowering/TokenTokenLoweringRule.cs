using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a TokenToken composite to a single ParamSource over TokenTokenCompositeSearchParam --
/// components[0] compares Code1, components[1] compares Code2, both via TokenColumnEquality.
/// </summary>
public static class TokenTokenLoweringRule
{
    public static CteDefinition.ParamSource Lower(
        SearchParameterInfo compositeParameter,
        IReadOnlyList<SearchParameterPredicateExpression> components,
        LeafContext context,
        short resourceTypeId)
    {
        var table = SqlCatalog.Default.Table("TokenTokenCompositeSearchParam");
        var predicate = new Predicate.And(
            TokenColumnEquality.Build(table, "Code1", (TokenSearchValue)components[0].Value, context),
            TokenColumnEquality.Build(table, "Code2", (TokenSearchValue)components[1].Value, context));

        return new CteDefinition.ParamSource(table, resourceTypeId, context.SearchParamId(compositeParameter), predicate);
    }
}
