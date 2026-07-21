// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;

namespace Ignixa.Search.Expressions.Parsers.Syntax;

/// <summary>A scanned <c>_not-referenced</c> key: an optional source resource type and reference path.</summary>
internal sealed record NotReferencedKeySyntax(string? SourceResourceType, string? ReferencePath) : SearchKeySyntax
{
    public bool Equals(NotReferencedKeySyntax? other)
        => other is not null
            && SourceResourceType == other.SourceResourceType
            && ReferencePath == other.ReferencePath;

    public override int GetHashCode() => HashCode.Combine(SourceResourceType, ReferencePath);
}
