// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;

namespace Ignixa.CliOnFhir.Indexing;

/// <summary>
/// In-memory search index over a <see cref="FolderCatalog"/>, grouped by resource type.
/// </summary>
/// <param name="Catalog">The catalog the index was built from.</param>
/// <param name="ByResourceType">Indexed resources per resource type, in catalog order.</param>
/// <param name="Diagnostics">Catalogued resources that could not be parsed or indexed.</param>
/// <param name="BuiltAt">When the index finished building.</param>
public sealed record FolderIndex(
    FolderCatalog Catalog,
    IReadOnlyDictionary<string, IReadOnlyList<IndexedResource>> ByResourceType,
    IReadOnlyList<CatalogDiagnostic> Diagnostics,
    DateTimeOffset BuiltAt)
{
    public int ResourceCount => ByResourceType.Values.Sum(list => list.Count);

    public long IndexEntryCount => ByResourceType.Values.Sum(list => list.Sum(resource => (long)resource.Index.Count));
}
