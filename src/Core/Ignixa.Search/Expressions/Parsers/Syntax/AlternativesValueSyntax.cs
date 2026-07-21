// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Collections.Immutable;

namespace Ignixa.Search.Expressions.Parsers.Syntax;

/// <summary>Comma-separated value alternatives — an OR over the items (e.g. <c>a,b,c</c>).</summary>
internal sealed record AlternativesValueSyntax(
    ImmutableArray<SearchValueSyntax> Items) : SearchValueSyntax
{
    public bool Equals(AlternativesValueSyntax? other)
        => other is not null && Items == other.Items;

    public override int GetHashCode() => Items.GetHashCode();
}
