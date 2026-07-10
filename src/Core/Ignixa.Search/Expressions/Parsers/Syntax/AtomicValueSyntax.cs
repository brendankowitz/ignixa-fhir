// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Expressions.Parsers.Syntax;

internal sealed record AtomicValueSyntax(
    string RawText,
    SearchComparator Comparator) : SearchValueSyntax;
