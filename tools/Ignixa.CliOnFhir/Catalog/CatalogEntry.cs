// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Catalog;

/// <summary>
/// Where one resource lives on disk: the file, and the byte range of its JSON within that file.
/// A single-resource file has offset 0 and the file's length; an NDJSON line or a Bundle entry has
/// the range of that line or of <c>entry.resource</c>, so the resource can be read without parsing
/// the rest of the file.
/// </summary>
/// <param name="ResourceType">The resource's <c>resourceType</c>.</param>
/// <param name="Id">The resource's <c>id</c>, or a synthetic id when <paramref name="HasSyntheticId"/> is true.</param>
/// <param name="FilePath">Path relative to the catalog root, with forward slashes.</param>
/// <param name="Offset">Byte offset of the resource JSON within the file.</param>
/// <param name="Length">Byte length of the resource JSON.</param>
/// <param name="Line">1-based line number for NDJSON entries; null otherwise.</param>
/// <param name="HasSyntheticId">
/// True when the resource had no <c>id</c> and one was derived: from its Bundle entry's RESTful <c>fullUrl</c> when that names
/// its type (R4 bundle.html §2.36.4.1), otherwise a hash of its location.
/// </param>
/// <param name="FullUrl">The Bundle entry's <c>fullUrl</c>; null outside a Bundle or when the entry has none.</param>
public sealed record CatalogEntry(
    string ResourceType,
    string Id,
    string FilePath,
    long Offset,
    int Length,
    int? Line,
    bool HasSyntheticId,
    string? FullUrl = null);
