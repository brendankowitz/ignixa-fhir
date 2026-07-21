// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Collections.Immutable;

namespace Ignixa.Search.Expressions.Parsers.Syntax;

/// <summary>A <c>$</c>-separated composite value — one atomic component per composite slot.</summary>
internal sealed record CompositeValueSyntax(
    ImmutableArray<AtomicValueSyntax> Components) : SearchValueSyntax
{
    public bool Equals(CompositeValueSyntax? other)
        => other is not null && Components == other.Components;

    public override int GetHashCode() => Components.GetHashCode();
}
