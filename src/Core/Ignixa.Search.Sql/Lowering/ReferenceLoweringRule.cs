using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a Reference search value to a ParamSource over ReferenceSearchParam. Handles the common
/// relative-reference case (resource type + resource id). Absolute/external references (a non-null
/// <see cref="ReferenceSearchValue.BaseUri"/>) and ReferenceResourceVersion are out of scope for this
/// rule; rather than silently ignoring BaseUri and producing an over-broad predicate that would also
/// match unrelated local references with the same type/id, this rule throws <see cref="NotSupportedException"/>
/// for absolute references.
/// </summary>
public static class ReferenceLoweringRule
{
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, ReferenceSearchValue value, LeafContext context, short resourceTypeId)
    {
        if (value.BaseUri is not null)
        {
            throw new NotSupportedException(
                $"Absolute/external reference search (BaseUri '{value.BaseUri}') is not supported by ReferenceLoweringRule. " +
                "Matching on BaseUri would require distinguishing it from local references with the same ResourceType/ResourceId, which this rule does not yet implement.");
        }

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

        return new CteDefinition.ParamSource(table, resourceTypeId, context.SearchParamId(predicate.Parameter), combined);
    }
}
