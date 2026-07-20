// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

namespace Ignixa.Search.Expressions.Parsers.Syntax;

/// <summary>A scanned <c>:of-type</c> value: the identifier type system, the type code, and the identifier value.</summary>
internal sealed record OfTypeValueSyntax(
    string TypeSystem,
    string TypeCode,
    string IdentifierValue) : SearchValueSyntax;
