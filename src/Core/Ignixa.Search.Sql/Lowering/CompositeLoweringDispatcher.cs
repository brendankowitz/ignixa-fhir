using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Dispatches a composite's ordered components to their tier-1 composite lowering rule, by the
/// runtime type of each component's wrapped ISearchValue. Orders by Position first -- callers
/// (real binder output, and this plan's own tests) are not required to hand components in order.
/// Only TokenToken and TokenNumberNumber are wired; every other composite table (TokenString,
/// TokenQuantity, TokenDateTime, ReferenceToken) throws NotSupportedException, matching
/// LeafLoweringDispatcher's precedent of a loud, explicit gap over a silent wrong answer.
/// </summary>
public static class CompositeLoweringDispatcher
{
    public static CteDefinition.ParamSource Lower(
        SearchParameterInfo compositeParameter,
        IReadOnlyList<CompositeComponentExpression> components,
        LeafContext context)
    {
        var ordered = components.OrderBy(c => c.Position).ToList();
        var predicates = new SearchParameterPredicateExpression[ordered.Count];
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].WrappedExpression is not SearchParameterPredicateExpression predicate)
            {
                throw new NotSupportedException(
                    $"Composite component at position {ordered[i].Position} on '{compositeParameter.Code}' wraps a " +
                    $"{ordered[i].WrappedExpression.GetType().Name}, not a SearchParameterPredicateExpression -- only " +
                    "single-valued components are supported (a component with its own comma-separated alternatives is not).");
            }

            predicates[i] = predicate;
        }

        return predicates.Select(p => p.Value).ToArray() switch
        {
            [TokenSearchValue, TokenSearchValue] => TokenTokenLoweringRule.Lower(compositeParameter, predicates, context),
            [TokenSearchValue, NumberSearchValue, NumberSearchValue] => TokenNumberNumberLoweringRule.Lower(compositeParameter, predicates, context),
            var values => throw new NotSupportedException(
                $"No composite lowering rule for component value types [{string.Join(", ", values.Select(v => v.GetType().Name))}] " +
                $"on composite parameter '{compositeParameter.Code}'."),
        };
    }
}
