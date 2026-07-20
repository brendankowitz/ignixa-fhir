using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering.Leaf;

/// <summary>
/// Lowers a Uri search value to a ParamSource over UriSearchParam — plain (no-modifier) equality only.
/// The :above/:below hierarchical modifiers are not implemented and throw rather than match without the
/// hierarchy constraint. Version/Fragment are not part of the base UriSearchParam table, so this rule
/// covers the base Uri column only.
/// </summary>
public static class UriLoweringRule
{
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, UriSearchValue value, LeafContext context, short resourceTypeId)
    {
        if (predicate.Modifier?.SearchModifierCode is SearchModifierCode.Above or SearchModifierCode.Below)
        {
            throw new NotSupportedException(
                $"Uri search with modifier '{predicate.Modifier.SearchModifierCode}' (hierarchical matching) is not " +
                "supported yet -- this rule only implements plain equality.");
        }

        var table = SqlCatalog.Default.Table("UriSearchParam");
        var column = new SqlColumnRef(table.TableName, "Uri");
        var predicateExpr = new Predicate.Equal(column, context.Parameter(value.Uri));

        return new CteDefinition.ParamSource(table, resourceTypeId, context.SearchParamId(predicate.Parameter), predicateExpr);
    }
}
