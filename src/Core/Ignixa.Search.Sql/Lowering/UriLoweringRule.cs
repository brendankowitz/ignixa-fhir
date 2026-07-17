using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a Uri search value to a ParamSource over UriSearchParam. Plain (no-modifier) equality only --
/// :above/:below (hierarchical URI matching) are not implemented and throw rather than silently
/// matching without the hierarchy constraint. Version/Fragment (canonical-URL extension fields) are
/// not in 97.sql's base UriSearchParam table -- they're populated via a separate post-merge extension
/// path -- so this rule, like SqlCatalog's UriSearchParam entry, covers only the base Uri column.
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
