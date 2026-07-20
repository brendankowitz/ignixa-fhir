// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

namespace Ignixa.Search.Expressions.Parsers;

/// <summary>
/// A public, serializable projection of one scanned syntax node. The scanner's own types stay internal;
/// this is the shape a trace or tooling consumes. Ancestry is resolved by span containment, ties by depth.
/// </summary>
public sealed record SyntaxNode(string Kind, SourceSpan Span, IReadOnlyList<SyntaxNode> Children);
