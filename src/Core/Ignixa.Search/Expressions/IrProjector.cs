// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Diagnostics.CodeAnalysis;

namespace Ignixa.Search.Expressions;

/// <summary>
/// Flattens a typed IR subtree into <see cref="IrRow"/>s — the counterpart to
/// <see cref="Parsers.SyntaxProjector"/>, which does the same for the scanned syntax.
/// </summary>
/// <remarks>
/// This is a view computed on demand, deliberately not a field on
/// <see cref="Parsing.ParameterTrace"/>: every traced parse would pay for it, and only a renderer ever
/// wants it. Callers project <c>trace.Ir</c> at the point they need rows.
/// <para>
/// Covers the untyped field-level kinds as well as the typed ones: <c>:text</c> and <c>:of-type</c> bind
/// through <c>SearchValueExpressionBuilderHelper</c> and so put a bare <see cref="StringExpression"/> or a
/// multiary of them into a parameter's IR. Those are ordinary FHIR searches, not exotica, so refusing to
/// project them would make <see cref="Describe"/> throw on real traffic. The remaining field-level kinds
/// (<see cref="BinaryExpression"/>, <see cref="MissingFieldExpression"/>) have no binder path into a traced
/// IR — the legacy factories that make them feed the EF query generator, not a parse — so they are left to
/// the loud <see cref="NotSupportedException"/> arm rather than given tokens nothing can produce.
/// </para>
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

    /// <summary>
    /// <see cref="Describe"/> for callers that must not fail the request over an unprojectable node —
    /// returns <see langword="false"/>, no rows, and the reason where <see cref="Describe"/> would throw.
    /// </summary>
    /// <remarks>
    /// The strict overload stays the default on purpose. A partial tree misrepresents what the server will
    /// execute, so anything asserting the projection is complete — tests, golden output — must keep using
    /// <see cref="Describe"/> and let it throw. This exists so a renderer showing a trace alongside a
    /// successful search can degrade to "no IR available" instead of turning a diagnostic into a 500.
    /// <para>
    /// <paramref name="unsupportedReason"/> is not optional politeness: it names the node kind that could
    /// not be projected. Without it a blank IR panel is indistinguishable from a rendering bug, and the
    /// one fact needed to diagnose it — which shape the projector does not cover — is exactly what the
    /// swallowed exception was carrying.
    /// </para>
    /// </remarks>
    /// <returns>
    /// <see langword="true"/> and the complete rows, or <see langword="false"/> and an empty list. Never a
    /// partial projection — a half-drawn tree is the failure mode this whole type is built to avoid.
    /// </returns>
    public static bool TryDescribe(
        Expression node,
        out IReadOnlyList<IrRow> rows,
        [NotNullWhen(false)] out string? unsupportedReason)
    {
        ArgumentNullException.ThrowIfNull(node);

        try
        {
            rows = Describe(node);
            unsupportedReason = null;
            return true;
        }
        catch (NotSupportedException ex)
        {
            rows = [];
            unsupportedReason = ex.Message;
            return false;
        }
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
        StringExpression => "stringField",
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
