using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering.Composite;

/// <summary>
/// Lowers a ReferenceToken composite to a single ParamSource over ReferenceTokenCompositeSearchParam.
/// Finds the Reference and Token components by runtime ISearchValue type, not array index, because some
/// definitions swap the component order. Routes the reference slot through <see cref="ReferenceColumnEquality"/>
/// and the token slot through <see cref="TokenColumnEquality"/>.
/// </summary>
public static class ReferenceTokenLoweringRule
{
    public static CteDefinition.ParamSource Lower(
        SearchParameterInfo compositeParameter,
        IReadOnlyList<SearchParameterPredicateExpression> components,
        LeafContext context,
        short resourceTypeId)
    {
        var referenceComponent = components.FirstOrDefault(c => c.Value is ReferenceSearchValue)
            ?? throw new NotSupportedException($"ReferenceToken composite '{compositeParameter.Code}' has no Reference-typed component.");
        var tokenComponent = components.FirstOrDefault(c => c.Value is TokenSearchValue)
            ?? throw new NotSupportedException($"ReferenceToken composite '{compositeParameter.Code}' has no Token-typed component.");

        var table = SqlCatalog.Default.Table("ReferenceTokenCompositeSearchParam");

        var referencePredicate = ReferenceColumnEquality.Build(
            table, "BaseUri1", "ReferenceResourceTypeId1", "ReferenceResourceId1",
            (ReferenceSearchValue)referenceComponent.Value, context);
        var tokenPredicate = TokenColumnEquality.Build(table, "SystemId2", "Code2", (TokenSearchValue)tokenComponent.Value, context);

        var predicate = new Predicate.And(referencePredicate, tokenPredicate);
        return new CteDefinition.ParamSource(table, resourceTypeId, context.SearchParamId(compositeParameter), predicate);
    }
}
