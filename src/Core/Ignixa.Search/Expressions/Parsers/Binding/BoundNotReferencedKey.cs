// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

namespace Ignixa.Search.Expressions.Parsers.Binding;

/// <summary>A bound <c>_not-referenced</c> key: the optional source resource type and reference path.</summary>
internal sealed record BoundNotReferencedKey(string? SourceResourceType, string? ReferencePath) : BoundSearchKey;
