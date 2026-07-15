// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Collections.Immutable;

namespace Ignixa.Search.Expressions.Parsers.Syntax;

internal sealed record CompositeValueSyntax(
    ImmutableArray<AtomicValueSyntax> Components) : SearchValueSyntax;
