// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Catalog;

/// <summary>A scanned file as it was when the catalog read it; the basis for detecting later changes.</summary>
/// <param name="Path">Path relative to the catalog root, with forward slashes.</param>
/// <param name="Length">Size in bytes.</param>
/// <param name="LastWriteTimeUtcTicks">Last-write time as UTC ticks, kept exact so equality is a plain comparison.</param>
public sealed record CatalogFile(string Path, long Length, long LastWriteTimeUtcTicks);
