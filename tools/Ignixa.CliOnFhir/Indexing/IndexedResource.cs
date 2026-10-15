// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.Search.Indexing;

namespace Ignixa.CliOnFhir.Indexing;

/// <summary>A catalogued resource with its extracted search index.</summary>
/// <param name="Key">The resource's type and catalog id.</param>
/// <param name="Entry">Where the resource was catalogued.</param>
/// <param name="Index">Its search index entries, extracted after its Bundle references were resolved.</param>
/// <param name="BundleReferences">The Bundle references resolved, and the <c>urn:</c> ones left unresolved, before extraction.</param>
public sealed record IndexedResource(ResourceKey Key, CatalogEntry Entry, IReadOnlyCollection<SearchIndexEntry> Index, ReferenceResolution BundleReferences);
