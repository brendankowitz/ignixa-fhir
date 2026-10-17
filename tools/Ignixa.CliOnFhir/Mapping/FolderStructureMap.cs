// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Reading;

namespace Ignixa.CliOnFhir.Mapping;

/// <summary>A StructureMap the folder holds, with what it is found by.</summary>
/// <param name="Entry">Its catalog entry.</param>
/// <param name="Json">Its JSON as written.</param>
/// <param name="Url">Its <c>url</c>, when it has one as text.</param>
/// <param name="Version">Its <c>version</c>, when it has one as text.</param>
internal sealed record FolderStructureMap(CatalogEntry Entry, string Json, string? Url, string? Version)
{
    /// <summary><c>StructureMap/id</c>, the id made safe.</summary>
    public string Reference => ResourceReader.Describe(Entry);

    /// <summary>Where it is in the folder.</summary>
    public EvidenceItem Evidence => new(Reference, Entry.FilePath, Entry.Line, Entry.Offset);

    /// <summary>Its text as a map to parse, as data of the folder.</summary>
    public LoadedMap Loaded => new(Json, LoadedMap.StructureMap, LoadedMap.FolderSource, FromFolder: true, Entry.Line is int line ? $"{Entry.FilePath}:{line}" : Entry.FilePath, Reference, Evidence);
}
