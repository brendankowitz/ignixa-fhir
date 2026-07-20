// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;

namespace Ignixa.Search.Parsing;

/// <summary>One parameter's trace: its position, source text, projected syntax, IR, and outcome.</summary>
public sealed record ParameterTrace(
    int Ordinal,
    string Key,
    string Value,
    SyntaxNode? Syntax,
    Expression? Ir,
    ParameterOutcome Outcome);
