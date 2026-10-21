// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

namespace Ignixa.Search.Expressions;

/// <summary>
/// Finds every <see cref="VectorSearchExpression"/> reachable in an expression tree, walking the whole
/// tree (not just top-level AND conjuncts) via <see cref="ExpressionRewriter{TContext}"/>'s existing
/// recursive dispatch -- the same collect-via-rewriter pattern
/// <c>Ignixa.Search.Sql.Symbols.SymbolCollectingVisitor</c> uses. Shared by two independent callers that
/// both need an authoritative answer to "is a semantic parameter present here": the Application layer's
/// <c>SemanticQueryPreparer</c> (to find the one node to embed, or reject more than one) and the SQL data
/// layer's <c>SqlServerCompiledSearchService</c> (to refuse combining a semantic query with export's
/// keyset continuation, which cannot carry a distance term across a page seam). A single definition
/// keeps those two checks from silently drifting apart as the expression grammar evolves.
/// </summary>
public static class VectorSearchExpressionLocator
{
    /// <summary>
    /// Returns every <see cref="VectorSearchExpression"/> in <paramref name="expression"/>, in tree
    /// traversal order. Empty when <paramref name="expression"/> is null or carries none.
    /// </summary>
    public static IReadOnlyList<VectorSearchExpression> FindAll(Expression? expression)
    {
        if (expression is null)
        {
            return [];
        }

        var collector = new Collector();
        expression.AcceptVisitor(collector, null);
        return collector.Found;
    }

    private sealed class Collector : ExpressionRewriter<object?>
    {
        public List<VectorSearchExpression> Found { get; } = [];

        public override Expression VisitVectorSearch(VectorSearchExpression expression, object? context)
        {
            Found.Add(expression);
            return expression;
        }
    }
}
