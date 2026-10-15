// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;

namespace Ignixa.CliOnFhir.Caching;

/// <summary>One resource type's persisted index, bound to the manifest it was written under.</summary>
/// <param name="ManifestId">Identity of the manifest the file belongs to; a different manifest makes it a plain miss.</param>
/// <param name="ResourceType">The indexed resource type.</param>
/// <param name="CatalogEntryCount">Catalogued resources of the type when it was indexed, to detect a truncated or mismatched file.</param>
/// <param name="BuiltAt">When the process that built the index finished.</param>
/// <param name="Diagnostics">The index's diagnostics, as built.</param>
/// <param name="Resources">The indexed resources, in catalog order.</param>
internal sealed record CachedTypeIndex(
    string ManifestId,
    string ResourceType,
    int CatalogEntryCount,
    DateTimeOffset BuiltAt,
    IReadOnlyList<CatalogDiagnostic> Diagnostics,
    IReadOnlyList<CachedResource> Resources);
