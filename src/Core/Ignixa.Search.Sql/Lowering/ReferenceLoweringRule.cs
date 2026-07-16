using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a Reference search value to a ParamSource over ReferenceSearchParam. Handles the common
/// relative-reference case (resource type + resource id); BaseUri (absolute/external references) and
/// ReferenceResourceVersion are out of scope for this rule -- documented, not silently dropped.
/// </summary>
public static class ReferenceLoweringRule
{
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, ReferenceSearchValue value, LeafContext context)
    {
        var table = SqlCatalog.Default.Table("ReferenceSearchParam");
        var idPredicate = new Predicate.Equal(
            new SqlColumnRef(table.TableName, "ReferenceResourceId"), context.Parameter(value.ResourceId));

        Predicate combined = string.IsNullOrEmpty(value.ResourceType)
            ? idPredicate
            : new Predicate.And(
                new Predicate.Equal(
                    new SqlColumnRef(table.TableName, "ReferenceResourceTypeId"),
                    context.Parameter(context.ResourceTypeId(value.ResourceType))),
                idPredicate);

        return new CteDefinition.ParamSource(table, context.SearchParamId(predicate.Parameter), combined);
    }
}
