// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Indexing;
using Ignixa.Search.Models;

namespace Ignixa.Search.Expressions.Parsers.Binding;

internal sealed record BoundParameterKey(SearchParameterInfo SearchParameter, SearchModifier? Modifier) : BoundSearchKey;
