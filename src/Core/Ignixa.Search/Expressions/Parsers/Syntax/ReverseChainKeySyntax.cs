// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

namespace Ignixa.Search.Expressions.Parsers.Syntax;

internal sealed record ReverseChainKeySyntax(string SourceResourceType, string ReferenceName, SearchKeySyntax Next) : SearchKeySyntax;
