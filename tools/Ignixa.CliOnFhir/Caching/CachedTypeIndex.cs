// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;

namespace Ignixa.CliOnFhir.Caching;

/// <summary>One resource type's persisted index, bound to the manifest it was written under.</summary>
/// <param name="ManifestId">Identity of the manifest the file belongs to; a different manifest makes it a plain miss.</param>
/// <param name="IndexedType">The indexed resource type. Deliberately not named <c>resourceType</c>: a cache file must never sniff as a FHIR resource.</param>
/// <param name="CatalogEntryCount">Catalogued resources of the type when it was indexed, to detect a truncated or mismatched file.</param>
/// <param name="BuiltAt">When the process that built the index finished.</param>
/// <param name="Diagnostics">The index's diagnostics, as built.</param>
/// <param name="Resources">The indexed resources, in catalog order.</param>
internal sealed record CachedTypeIndex(
    string ManifestId,
    string IndexedType,
    int CatalogEntryCount,
    DateTimeOffset BuiltAt,
    IReadOnlyList<CatalogDiagnostic> Diagnostics,
    IReadOnlyList<CachedResource> Resources)
{
    /// <summary>
    /// Checks that every member the rest of the cache relies on is present. JSON can hold a null for any member, so
    /// a deserialized instance is not trusted until this passes.
    /// </summary>
    /// <exception cref="InvalidDataException">A required member is missing, or a resource appears twice.</exception>
    public void Validate()
    {
        if (ManifestId is null || IndexedType is null || Diagnostics is null || Resources is null)
        {
            throw new InvalidDataException("a required member of the file is missing.");
        }

        if (Diagnostics.Any(diagnostic => diagnostic is null || diagnostic.Code is null || diagnostic.FilePath is null || diagnostic.Message is null))
        {
            throw new InvalidDataException("a diagnostic in the file is incomplete.");
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (CachedResource resource in Resources)
        {
            if (resource?.Id is null || resource.Entries is null || resource.Entries.Any(entry => entry?.Code is null || entry.Value is null))
            {
                throw new InvalidDataException("a resource in the file is incomplete.");
            }

            if (!ids.Add(resource.Id))
            {
                throw new InvalidDataException($"{IndexedType}/{resource.Id} appears twice.");
            }
        }
    }
}
