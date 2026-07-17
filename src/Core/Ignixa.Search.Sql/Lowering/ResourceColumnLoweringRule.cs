using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers _id/_type/_lastUpdated -- ordinary resource-column search parameters that bind through the
/// same SearchExpressionBinder/BindAtomic pipeline as any other parameter (no special node type), but
/// target dbo.Resource's own columns via QueryPlan.OuterPredicate, not a ParamSource table. Returns
/// null for any other parameter code -- the caller (Lower.Run's extraction pass) treats null as "not a
/// resource-column predicate, dispatch it normally." _lastUpdated's arm is added in a later increment
/// task; this file starts with _id/_type only.
/// </summary>
public static class ResourceColumnLoweringRule
{
    public static Predicate? TryLower(SearchParameterPredicateExpression predicate, LeafContext context) => predicate.Parameter.Code switch
    {
        "_id" => IdEquals(predicate, context),
        "_type" => TypeEquals(predicate, context),
        _ => null,
    };

    private static Predicate IdEquals(SearchParameterPredicateExpression predicate, LeafContext context)
    {
        var value = (TokenSearchValue)predicate.Value;
        if (value.System is not null)
        {
            throw new NotSupportedException("_id does not support a System qualifier.");
        }

        if (string.IsNullOrEmpty(value.Code))
        {
            throw new NotSupportedException("_id requires a non-empty value.");
        }

        var table = SqlCatalog.Default.Table("Resource");
        return new Predicate.Equal(new SqlColumnRef(table.TableName, "ResourceId"), context.Parameter(value.Code));
    }

    private static Predicate TypeEquals(SearchParameterPredicateExpression predicate, LeafContext context)
    {
        var value = (TokenSearchValue)predicate.Value;
        if (value.System is not null)
        {
            throw new NotSupportedException("_type does not support a System qualifier.");
        }

        if (string.IsNullOrEmpty(value.Code))
        {
            throw new NotSupportedException("_type requires a non-empty resource type name.");
        }

        var table = SqlCatalog.Default.Table("Resource");
        return new Predicate.Equal(new SqlColumnRef(table.TableName, "ResourceTypeId"), context.Parameter(context.ResourceTypeId(value.Code)));
    }
}
