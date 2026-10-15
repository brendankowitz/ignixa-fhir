// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.CliOnFhir.Caching;

/// <summary>One persisted search index entry: the parameter's code, re-bound to the real definition on load, and its value.</summary>
internal sealed record CachedEntry(
    string Code,
    [property: JsonConverter(typeof(CacheSearchValueConverter))] ISearchValue Value);
