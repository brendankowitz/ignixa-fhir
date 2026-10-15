// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>What reading one source found.</summary>
/// <param name="FilesRead">JSON files (or folder resources) read.</param>
/// <param name="OtherResources">Resources of other types (StructureDefinitions, SearchParameters, ...), not loaded.</param>
/// <param name="Unusable">Files that were not JSON objects, or terminology resources without a url.</param>
/// <param name="FilesIgnored">Package entries outside <c>package/</c>, in subfolders, or not regular files, which are never read.</param>
public sealed record SourceCounts(int FilesRead, int OtherResources, int Unusable, int FilesIgnored);
