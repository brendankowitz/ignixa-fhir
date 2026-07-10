// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions.Parsers.Syntax;
using Superpower;
using Superpower.Parsers;
using Superpower.Tokenizers;

namespace Ignixa.Search.Expressions.Parsers;

internal static class SearchKeyTokenizer
{
    internal static Tokenizer<SearchKeyTokenKind> Instance { get; } = new TokenizerBuilder<SearchKeyTokenKind>()
        .Match(Span.Regex(@"[A-Za-z_][A-Za-z0-9_-]*"), SearchKeyTokenKind.Identifier, requireDelimiters: false)
        .Match(Character.EqualTo(':'), SearchKeyTokenKind.Colon)
        .Match(Character.EqualTo('.'), SearchKeyTokenKind.Dot)
        .Match(Character.EqualTo('*'), SearchKeyTokenKind.Asterisk)
        .Build();
}
