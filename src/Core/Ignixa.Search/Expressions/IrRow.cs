// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

namespace Ignixa.Search.Expressions;

/// <summary>
/// One flattened IR node: a stable kind token, a one-line description, and its nesting depth.
/// </summary>
/// <remarks>
/// A parsed <see cref="Expression"/> is a live object graph — it holds resolved
/// <see cref="Models.SearchParameterInfo"/> and <see cref="Indexing.SearchValues.ISearchValue"/> instances and cannot
/// cross a wire or be rendered without the caller knowing every node type. This is the serializable view:
/// enough to draw an indented tree with a per-node kind chip, and nothing else.
/// <para>
/// In a sequence produced by <see cref="IrProjector.Describe"/>, <see cref="Depth"/> is 0 on the first row
/// and changes by at most +1 from the row before it, because rows arrive in pre-order. A renderer can
/// therefore indent straight from <see cref="Depth"/> without tracking a stack. That is a property of the
/// producer and of the sequence, not of this type: a single row can only promise its own depth is
/// non-negative, which the constructor enforces. A renderer handed rows from anywhere else still has to
/// check.
/// </para>
/// <para>
/// <see cref="Kind"/> is a token a renderer switches on, so it must be non-empty. <see cref="Text"/> is
/// display text and may legitimately be empty — <see cref="IrProjector"/> falls back to an empty string
/// rather than failing a trace over a node that renders blank, and a diagnostics projection throwing is
/// worse than a blank line.
/// </para>
/// <para>
/// The properties are get-only rather than <c>init</c>: a <c>with</c> expression copies through the
/// compiler-generated copy constructor and would skip the checks below entirely.
/// </para>
/// </remarks>
public sealed record IrRow
{
    public IrRow(string kind, string text, int depth)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        Kind = kind;
        Text = text;
        Depth = depth;
    }

    public string Kind { get; }

    public string Text { get; }

    public int Depth { get; }

    public void Deconstruct(out string kind, out string text, out int depth)
    {
        kind = Kind;
        text = Text;
        depth = Depth;
    }
}
