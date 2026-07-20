// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Collections.Immutable;
using Ignixa.Search.Models;

namespace Ignixa.Search.Expressions.Parsers.Binding;

/// <summary>A bound chain link: the resolved reference parameter, the source and target resource types, the direction, and the next bound key.</summary>
internal sealed record BoundChainKey(
    ImmutableArray<string> ResourceTypes,
    SearchParameterInfo ReferenceSearchParameter,
    ImmutableArray<string> TargetResourceTypes,
    bool Reversed,
    BoundSearchKey Next) : BoundSearchKey;
