// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Expressions.Parsers.Syntax;

/// <summary>A single scanned value with its comparator prefix separated out (e.g. <c>gt2000</c> → text <c>2000</c>, comparator <c>gt</c>).</summary>
internal sealed record AtomicValueSyntax(
    string RawText,
    SearchComparator Comparator) : SearchValueSyntax
{
    public bool Equals(AtomicValueSyntax? other)
        => other is not null && RawText == other.RawText && Comparator == other.Comparator;

    public override int GetHashCode() => HashCode.Combine(RawText, Comparator);
}
