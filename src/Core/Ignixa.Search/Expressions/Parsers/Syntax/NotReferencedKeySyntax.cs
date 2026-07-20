// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

namespace Ignixa.Search.Expressions.Parsers.Syntax;

/// <summary>A scanned <c>_not-referenced</c> key: an optional source resource type and reference path.</summary>
internal sealed record NotReferencedKeySyntax(string? SourceResourceType, string? ReferencePath) : SearchKeySyntax;
