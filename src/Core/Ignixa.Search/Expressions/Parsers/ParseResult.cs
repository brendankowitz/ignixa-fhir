// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

namespace Ignixa.Search.Expressions.Parsers;

/// <summary>
/// A parsed search parameter plus its projected syntax. <see cref="ValueSyntax"/> is null for shapes with
/// no value tree, such as <c>_not-referenced</c> and <c>_include</c>/<c>_revinclude</c>.
/// </summary>
public sealed record ParseResult(Expression Expression, SyntaxNode KeySyntax, SyntaxNode? ValueSyntax);
