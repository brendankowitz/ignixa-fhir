// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Catalog;

/// <summary>
/// The <c>fullUrl</c> of a catalogued Bundle entry and the resource it names, so references to the entry
/// (<c>urn:uuid:...</c>, or an absolute URL) can be resolved to the catalogued <c>Type/id</c>.
/// </summary>
/// <param name="FullUrl">The entry's <c>fullUrl</c>, as written.</param>
/// <param name="FilePath">The Bundle's path relative to the catalog root, with forward slashes.</param>
/// <param name="ResourceType">The entry resource's type.</param>
/// <param name="Id">The catalogued id: the resource's own, a synthetic one, or for a duplicate the id of the occurrence the catalog kept (the same <c>Type/id</c>).</param>
public sealed record BundleFullUrl(string FullUrl, string FilePath, string ResourceType, string Id);
