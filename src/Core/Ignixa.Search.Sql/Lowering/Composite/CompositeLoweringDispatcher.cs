using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering.Leaf;

namespace Ignixa.Search.Sql.Lowering.Composite;

/// <summary>
/// Dispatches a composite's components to their composite lowering rule by the runtime types of the wrapped
/// ISearchValues, ordering by Position first since callers need not pass them in order. TokenToken,
/// TokenNumberNumber, TokenString, TokenQuantity, TokenDateTime, and ReferenceToken (either order) are
/// wired; any other combination throws — a loud gap over a silent wrong answer.
/// </summary>
internal static class CompositeLoweringDispatcher
{
    public static CteDefinition.ParamSource Lower(
        SearchParameterInfo compositeParameter,
        IReadOnlyList<CompositeComponentExpression> components,
        LeafContext context,
        short? resourceTypeId)
    {
        var ordered = components.OrderBy(c => c.Position).ToList();
        try
        {
            return LowerCore(compositeParameter, ordered, context, resourceTypeId);
        }
        catch (Exception ex) when (LeafLoweringDispatcher.IsUnattributedLoweringFailure(ex))
        {
            LeafLoweringDispatcher.Enrich(ex, compositeParameter, ordered.Count > 0 ? ordered[0].Span : null);
            throw;
        }
    }

    private static CteDefinition.ParamSource LowerCore(
        SearchParameterInfo compositeParameter,
        IReadOnlyList<CompositeComponentExpression> ordered,
        LeafContext context,
        short? resourceTypeId)
    {
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
            [TokenSearchValue, TokenSearchValue] => TokenTokenLoweringRule.Lower(compositeParameter, predicates, context, resourceTypeId),
            [TokenSearchValue, NumberSearchValue, NumberSearchValue] => TokenNumberNumberLoweringRule.Lower(compositeParameter, predicates, context, resourceTypeId),
            [TokenSearchValue, StringSearchValue] => TokenStringLoweringRule.Lower(compositeParameter, predicates, context, resourceTypeId),
            [TokenSearchValue, QuantitySearchValue] => TokenQuantityLoweringRule.Lower(compositeParameter, predicates, context, resourceTypeId),
            [TokenSearchValue, DateTimeSearchValue] => TokenDateTimeLoweringRule.Lower(compositeParameter, predicates, context, resourceTypeId),
            [ReferenceSearchValue, TokenSearchValue] => ReferenceTokenLoweringRule.Lower(compositeParameter, predicates, context, resourceTypeId),
            [TokenSearchValue, ReferenceSearchValue] => ReferenceTokenLoweringRule.Lower(compositeParameter, predicates, context, resourceTypeId),
            var values => throw new NotSupportedException(
                $"No composite lowering rule for component value types [{string.Join(", ", values.Select(v => v.GetType().Name))}] " +
                $"on composite parameter '{compositeParameter.Code}'."),
        };
    }
}
