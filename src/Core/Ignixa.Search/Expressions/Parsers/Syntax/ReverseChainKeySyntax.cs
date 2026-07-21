// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;

namespace Ignixa.Search.Expressions.Parsers.Syntax;

/// <summary>A scanned reverse chain key (<c>_has:Source:reference:…</c>): the source resource type, the reference name, and the chained key.</summary>
internal sealed record ReverseChainKeySyntax(string SourceResourceType, string ReferenceName, SearchKeySyntax Next) : SearchKeySyntax
{
    public bool Equals(ReverseChainKeySyntax? other)
        => other is not null
            && SourceResourceType == other.SourceResourceType
            && ReferenceName == other.ReferenceName
            && Next == other.Next;

    public override int GetHashCode() => HashCode.Combine(SourceResourceType, ReferenceName, Next);
}
