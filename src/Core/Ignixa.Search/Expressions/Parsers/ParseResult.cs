// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Expressions.Parsers;

/// <summary>
/// A parsed search parameter plus its projected syntax. <see cref="ValueSyntax"/> is null for shapes with
/// no value tree, such as <c>_not-referenced</c> and <c>_include</c>/<c>_revinclude</c>.
/// </summary>
/// <param name="Expression">The bound expression.</param>
/// <param name="KeySyntax">Projected syntax for the parameter key.</param>
/// <param name="ValueSyntax">Projected syntax for the parameter value, where the shape has one.</param>
/// <param name="DataType">
/// The declared type of the search parameter the value was bound against — for a chain, the terminal
/// parameter, since that is the one the value is matched with. Null for shapes that bind no value
/// parameter (<c>_not-referenced</c>). Captured here because the binder already resolved it; recovering it
/// downstream means walking the expression graph and re-deriving what was known at parse time.
/// </param>
public sealed record ParseResult(
    Expression Expression,
    SyntaxNode KeySyntax,
    SyntaxNode? ValueSyntax,
    SearchParamType? DataType);
