// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;

namespace Ignixa.CliOnFhir.Indexing;

/// <summary>
/// In-memory search index over the catalogued resources of one resource type.
/// </summary>
/// <param name="ResourceType">The indexed resource type.</param>
/// <param name="Resources">Indexed resources, in catalog order. Empty when the folder holds none of this type.</param>
/// <param name="Diagnostics">Resources that could not be parsed, and search parameters that could not be extracted.</param>
/// <param name="BuiltAt">When the index finished building.</param>
/// <param name="BuildMilliseconds">Wall time the build took, excluding the one-time indexer load (<see cref="Session.FolderSession.IndexerLoadMilliseconds"/>).</param>
public sealed record ResourceTypeIndex(
    string ResourceType,
    IReadOnlyList<IndexedResource> Resources,
    IReadOnlyList<CatalogDiagnostic> Diagnostics,
    DateTimeOffset BuiltAt,
    long BuildMilliseconds)
{
    public long IndexEntryCount => Resources.Sum(resource => (long)resource.Index.Count);
}
