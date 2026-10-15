// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.Search.Indexing;

namespace Ignixa.CliOnFhir.Indexing;

/// <summary>A catalogued resource with its extracted search index.</summary>
public sealed record IndexedResource(ResourceKey Key, CatalogEntry Entry, IReadOnlyCollection<SearchIndexEntry> Index);
