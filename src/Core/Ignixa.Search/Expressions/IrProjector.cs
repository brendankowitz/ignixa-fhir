// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

namespace Ignixa.Search.Expressions;

/// <summary>
/// Flattens a typed IR subtree into <see cref="IrRow"/>s — the counterpart to
/// <see cref="Parsers.SyntaxProjector"/>, which does the same for the scanned syntax.
/// </summary>
/// <remarks>
/// This is a view computed on demand, deliberately not a field on
/// <see cref="Parsing.ParameterTrace"/>: every traced parse would pay for it, and only a renderer ever
/// wants it. Callers project <c>trace.Ir</c> at the point they need rows.
/// </remarks>
public static class IrProjector
{
    /// <summary>
    /// Walks <paramref name="node"/> pre-order, yielding one row per node with <see cref="IrRow.Depth"/>
    /// counting nesting levels from zero at the root.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// A node kind this projection does not describe was reached. Deliberately loud rather than skipped:
    /// a silently dropped node renders a tree that misrepresents what the server will execute.
    /// </exception>
    public static IReadOnlyList<IrRow> Describe(Expression node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var rows = new List<IrRow>();
        Visit(node, 0, rows);
        return rows;
    }

    private static void Visit(Expression node, int depth, List<IrRow> rows)
    {
        rows.Add(new IrRow(KindOf(node), TextOf(node), depth));

        foreach (var child in ChildrenOf(node))
        {
            Visit(child, depth + 1, rows);
        }
    }

    private static string KindOf(Expression node) => node switch
    {
        MultiaryExpression m => m.MultiaryOperation == MultiaryOperator.And ? "and" : "or",
        UnionExpression => "union",
        NotExpression => "not",
        SearchParameterExpression => "param",
        ChainedExpression => "chain",
        CompositeComponentExpression => "composite",
        SearchParameterPredicateExpression => "predicate",
        MissingSearchParameterExpression => "missing",
        NotReferencedExpression => "notReferenced",
        IncludeExpression => "include",
        SortExpression => "sort",
        _ => throw new NotSupportedException($"No IR projection for {node.GetType().Name}."),
    };

    /// <summary>
    /// One line describing the node itself. Container kinds get the head of their own
    /// <see cref="object.ToString"/> only — the full form nests every descendant, which would repeat text
    /// the child rows already carry. Leaf kinds reuse <see cref="object.ToString"/> unchanged.
    /// </summary>
    private static string TextOf(Expression node) => node switch
    {
        MultiaryExpression m => m.MultiaryOperation.ToString(),
        UnionExpression u => $"Union {u.Operator}",
        NotExpression => "Not",
        SearchParameterExpression sp => $"Param {sp.Parameter.Code}",
        ChainedExpression c => $"{(c.Reversed ? "Reverse " : string.Empty)}Chain {c.ReferenceSearchParameter.Code}:{string.Join(", ", c.TargetResourceTypes)}",
        CompositeComponentExpression cc => $"Component[{cc.Position}] {cc.ComponentSearchParameter.Code}",
        _ => node.ToString() ?? string.Empty,
    };

    private static IReadOnlyList<Expression> ChildrenOf(Expression node) => node switch
    {
        MultiaryExpression m => m.Expressions,
        UnionExpression u => u.Expressions,
        NotExpression n => [n.Expression],
        SearchParameterExpression sp => [sp.Expression],
        ChainedExpression c => [c.Expression],
        CompositeComponentExpression cc => [cc.WrappedExpression],
        _ => [],
    };
}
