// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Application.Tests.Search;

/// <summary>
/// Proves ExpressionRewriter's default VisitCompositeComponent rebuilds the wrapper around a
/// rewritten inner expression instead of stripping it - a naive unwrap-and-return would silently
/// discard the CompositeComponentExpression identity on any rewrite that changes the wrapped
/// expression. Task 3 adds the realistic end-to-end regression test via DateTimeEqualityRewriter.
/// </summary>
public class ExpressionRewriterCompositeComponentTests
{
    private static readonly SearchParameterInfo TokenComponentParam = new("code", "code", SearchParamType.Token);

    [Fact]
    public void GivenComponentWhoseInnerExpressionChanges_WhenRewritten_ThenWrapperIsRebuiltAroundNewInner()
    {
        var original = new CompositeComponentExpression(TokenComponentParam, 0, Expression.Equals(FieldName.TokenCode, 0, "a"));
        var replacement = Expression.Equals(FieldName.TokenCode, 0, "b");
        var rewriter = new ReplacingRewriter(replacement);

        var result = rewriter.VisitCompositeComponent(original, context: 0);

        var rebuilt = result.ShouldBeOfType<CompositeComponentExpression>();
        rebuilt.Position.ShouldBe(0);
        rebuilt.ComponentSearchParameter.ShouldBe(TokenComponentParam);
        rebuilt.WrappedExpression.ShouldBeSameAs(replacement);
    }

    [Fact]
    public void GivenComponentWhoseInnerExpressionDoesNotChange_WhenRewritten_ThenSameInstanceReturned()
    {
        var original = new CompositeComponentExpression(TokenComponentParam, 0, Expression.Equals(FieldName.TokenCode, 0, "a"));
        var rewriter = new NoOpRewriter();

        var result = rewriter.VisitCompositeComponent(original, context: 0);

        result.ShouldBeSameAs(original);
    }

    private sealed class ReplacingRewriter(Expression replacement) : ExpressionRewriter<int>
    {
        public override Expression VisitBinary(BinaryExpression expression, int context) => replacement;
    }

    private sealed class NoOpRewriter : ExpressionRewriter<int>
    {
    }
}
