// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

namespace Ignixa.Search.Expressions.Parsers.Syntax;

/// <summary>A scanned <c>:missing</c> value: whether the parameter must be absent (<c>true</c>) or present (<c>false</c>).</summary>
internal sealed record MissingValueSyntax(bool IsMissing) : SearchValueSyntax;
