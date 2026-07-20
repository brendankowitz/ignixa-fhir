// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;

namespace Ignixa.Search.Parsing;

/// <summary>
/// One parameter's trace: its position, source text, projected syntax, IR, and outcome.
/// </summary>
/// <remarks>
/// <see cref="KeySyntax"/> and <see cref="ValueSyntax"/> mirror <see cref="ParseResult"/>'s two syntax
/// projections. Structural provenance — chain segments, modifiers, include shape — lives in
/// <see cref="KeySyntax"/>; value structure — alternatives, composites, atomics — lives in
/// <see cref="ValueSyntax"/>. Both are nullable: <see cref="ValueSyntax"/> is legitimately null for
/// shapes with no value tree (<c>_not-referenced</c>, includes), and either may be null when a
/// parameter is <see cref="ParameterOutcome.Ignored"/> or <see cref="ParameterOutcome.Failed"/> before
/// parsing completes.
/// </remarks>
public sealed record ParameterTrace(
    int Ordinal,
    string Key,
    string Value,
    SyntaxNode? KeySyntax,
    SyntaxNode? ValueSyntax,
    Expression? Ir,
    ParameterOutcome Outcome);
