// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

namespace Ignixa.Search.Expressions.Parsers.Syntax;

/// <summary>A scanned ordinary search key: a parameter name and an optional modifier (e.g. <c>name:exact</c>).</summary>
internal sealed record ParameterKeySyntax(string Name, string? Modifier) : SearchKeySyntax;
