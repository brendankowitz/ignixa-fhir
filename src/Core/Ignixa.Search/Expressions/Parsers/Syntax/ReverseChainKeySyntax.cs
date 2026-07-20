// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

namespace Ignixa.Search.Expressions.Parsers.Syntax;

/// <summary>A scanned reverse chain key (<c>_has:Source:reference:…</c>): the source resource type, the reference name, and the chained key.</summary>
internal sealed record ReverseChainKeySyntax(string SourceResourceType, string ReferenceName, SearchKeySyntax Next) : SearchKeySyntax;
