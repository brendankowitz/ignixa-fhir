// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions;

namespace Ignixa.Search.Expressions.Parsers.Syntax;

/// <summary>The scanned structure of a search value (the right side of a search parameter), before it is typed.</summary>
internal abstract record SearchValueSyntax
{
    public SourceSpan Span { get; init; }
}
