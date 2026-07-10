// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Collections.Immutable;
using Ignixa.Search.Models;

namespace Ignixa.Search.Expressions.Parsers.Binding;

internal sealed record BoundIncludeKey(
    SearchParameterInfo? ReferenceSearchParameter,
    string SourceResourceType,
    string? TargetResourceType,
    ImmutableArray<string> ReferencedTypes,
    bool Wildcard) : BoundSearchKey;
