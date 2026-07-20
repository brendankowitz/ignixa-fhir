using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql.Lowering.Leaf;

/// <summary>
/// Dispatches a leaf predicate to its lowering rule by the runtime type of its ISearchValue. A composite
/// value has no leaf rule and throws — composites are lowered by <see cref="Composite.CompositeLoweringDispatcher"/>.
/// </summary>
public static class LeafLoweringDispatcher
{
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, LeafContext context, short resourceTypeId) => predicate.Value switch
    {
        StringSearchValue s => StringLoweringRule.Lower(predicate, s, context, resourceTypeId),
        TokenSearchValue t => TokenLoweringRule.Lower(predicate, t, context, resourceTypeId),
        ReferenceSearchValue r => ReferenceLoweringRule.Lower(predicate, r, context, resourceTypeId),
        UriSearchValue u => UriLoweringRule.Lower(predicate, u, context, resourceTypeId),
        NumberSearchValue n => NumberLoweringRule.Lower(predicate, n, context, resourceTypeId),
        QuantitySearchValue q => QuantityLoweringRule.Lower(predicate, q, context, resourceTypeId),
        DateTimeSearchValue d => DateTimeLoweringRule.Lower(predicate, d, context, resourceTypeId),
        _ => throw new NotSupportedException(
            $"No lowering rule for {predicate.Value.GetType().Name} -- composites are out of scope for this plan."),
    };
}
