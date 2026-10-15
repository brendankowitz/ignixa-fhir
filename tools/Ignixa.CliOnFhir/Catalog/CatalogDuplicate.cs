// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Catalog;

/// <summary>
/// A resource the catalog dropped because an earlier one had the same type and id: every tool answers from
/// <paramref name="Kept"/> only, so <paramref name="Ignored"/> is never searched or read.
/// </summary>
/// <param name="ResourceType">The shared resource type.</param>
/// <param name="Id">The shared id; for an id-less Bundle entry, the one its RESTful fullUrl gives it.</param>
/// <param name="Kept">Where the first occurrence, the one used, lives (<c>file:line</c> or <c>file@offset</c>).</param>
/// <param name="Ignored">Where the dropped occurrence lives.</param>
public sealed record CatalogDuplicate(string ResourceType, string Id, string Kept, string Ignored);
