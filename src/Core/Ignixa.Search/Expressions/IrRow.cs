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
/// </remarks>
public sealed record IrRow(string Kind, string Text, int Depth);
