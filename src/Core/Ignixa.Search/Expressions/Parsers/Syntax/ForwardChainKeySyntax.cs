// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

namespace Ignixa.Search.Expressions.Parsers.Syntax;

/// <summary>A scanned forward chain key (e.g. <c>subject:Patient.name</c>): a reference name, an optional target resource type, and the chained key.</summary>
internal sealed record ForwardChainKeySyntax(string ReferenceName, string? TargetResourceType, SearchKeySyntax Next) : SearchKeySyntax;
