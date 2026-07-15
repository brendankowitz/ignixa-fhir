// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions.Parsers;

namespace Ignixa.Search.Expressions;

/// <summary>
/// Converts the typed predicate tree (<see cref="SearchParameterPredicateExpression"/>,
/// <see cref="CompositeComponentExpression"/>) back to the old untyped field-level shape, for
/// consumers that haven't migrated to consume the typed tree directly.
/// See docs/superpowers/specs/2026-07-15-search-semantic-ir-design.md.
/// </summary>
public sealed class LegacyExpressionLowerer : ExpressionRewriter<object?>
{
    public override Expression VisitSearchParameterPredicate(SearchParameterPredicateExpression expression, object? context)
        => new SearchValueExpressionBuilderHelper().Build(expression.Parameter.Code, expression.Modifier, expression.Comparator, componentIndex: null, expression.Value);

    public override Expression VisitCompositeComponent(CompositeComponentExpression expression, object? context)
    {
        // The wrapped expression is expected to be a SearchParameterPredicateExpression (that's the
        // only thing BindComposite ever wraps, per task 4) -- lower it directly with this component's
        // Position, rather than lowering generically and re-stamping, since Build's own componentIndex
        // parameter already exists for exactly this.
        if (expression.WrappedExpression is SearchParameterPredicateExpression predicate)
        {
            return new SearchValueExpressionBuilderHelper().Build(predicate.Parameter.Code, predicate.Modifier, predicate.Comparator, expression.Position, predicate.Value);
        }

        throw new NotSupportedException($"{nameof(LegacyExpressionLowerer)} can only lower a {nameof(CompositeComponentExpression)} whose {nameof(CompositeComponentExpression.WrappedExpression)} is a {nameof(SearchParameterPredicateExpression)}, found {expression.WrappedExpression.GetType().Name}.");
    }
}
