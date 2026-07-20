// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

namespace Ignixa.Search.Expressions.Parsers.Syntax;

/// <summary>A scanned <c>_include</c>/<c>_revinclude</c> key: the source resource type, the search parameter name (or a wildcard), and an optional target type.</summary>
internal sealed record IncludeKeySyntax(
    string SourceResourceType,
    string? SearchParameterName,
    string? TargetResourceType,
    bool Wildcard) : SearchKeySyntax;
