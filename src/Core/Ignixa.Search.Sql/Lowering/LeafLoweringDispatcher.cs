using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Dispatches a leaf predicate to its tier-1 lowering rule by the runtime type of its ISearchValue.
/// Date/Number/Quantity and all composites throw -- out of scope for this plan (see this plan's
/// global constraints).
/// </summary>
public static class LeafLoweringDispatcher
{
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, LeafContext context) => predicate.Value switch
    {
        StringSearchValue s => StringLoweringRule.Lower(predicate, s, context),
        TokenSearchValue t => TokenLoweringRule.Lower(predicate, t, context),
        ReferenceSearchValue r => ReferenceLoweringRule.Lower(predicate, r, context),
        UriSearchValue u => UriLoweringRule.Lower(predicate, u, context),
        _ => throw new NotSupportedException(
            $"No lowering rule for {predicate.Value.GetType().Name} -- Date/Number/Quantity and composites are out of scope for this plan."),
    };
}
