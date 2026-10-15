// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Catalog;

/// <summary>
/// The result of one walk of a folder: every resource located, plus everything that could not be read.
/// </summary>
/// <param name="RootPath">Absolute path of the catalogued folder.</param>
/// <param name="Entries">Located resources, in deterministic file-then-position order; the first occurrence of a duplicate key wins.</param>
/// <param name="Diagnostics">Files or entries that could not be catalogued, unknown resource types and duplicate keys.</param>
/// <param name="FilesScanned">Number of files with a supported extension that were read.</param>
/// <param name="FilesSkipped">Number of files ignored because of their extension.</param>
/// <param name="ContainerBundles">Number of Bundles whose entries were catalogued instead of the Bundle itself.</param>
/// <param name="Files">Every scanned file with the size and last-write time it had when read, in walk order.</param>
/// <param name="BuiltAt">When the walk finished.</param>
/// <param name="FullUrls">The <c>fullUrl</c> of every catalogued Bundle entry that has one, in walk order, duplicates included.</param>
/// <param name="Duplicates">Every occurrence dropped for repeating an earlier type and id, in walk order; each also has a <c>duplicate-id</c> diagnostic.</param>
public sealed record FolderCatalog(
    string RootPath,
    IReadOnlyList<CatalogEntry> Entries,
    IReadOnlyList<CatalogDiagnostic> Diagnostics,
    int FilesScanned,
    int FilesSkipped,
    int ContainerBundles,
    IReadOnlyList<CatalogFile> Files,
    DateTimeOffset BuiltAt,
    IReadOnlyList<BundleFullUrl> FullUrls,
    IReadOnlyList<CatalogDuplicate> Duplicates);
