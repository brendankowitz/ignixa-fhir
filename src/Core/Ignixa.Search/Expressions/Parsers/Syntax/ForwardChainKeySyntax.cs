// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;

namespace Ignixa.Search.Expressions.Parsers.Syntax;

/// <summary>A scanned forward chain key (e.g. <c>subject:Patient.name</c>): a reference name, an optional target resource type, and the chained key.</summary>
internal sealed record ForwardChainKeySyntax(string ReferenceName, string? TargetResourceType, SearchKeySyntax Next) : SearchKeySyntax
{
    public bool Equals(ForwardChainKeySyntax? other)
        => other is not null
            && ReferenceName == other.ReferenceName
            && TargetResourceType == other.TargetResourceType
            && Next == other.Next;

    public override int GetHashCode() => HashCode.Combine(ReferenceName, TargetResourceType, Next);
}
