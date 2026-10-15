// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ignixa.CliOnFhir.Catalog;

/// <summary>
/// Walks a folder once and records where each FHIR resource lives. Single-resource JSON files,
/// NDJSON/JSONL files and Bundles (whose entries are catalogued individually) are recognized by
/// extension and then by content; nothing in the folder is modified.
/// </summary>
public static class FolderCatalogBuilder
{
    /// <summary>Directory name that is never catalogued; the index cache lives outside the folder, but a stray one is still skipped.</summary>
    public const string CacheDirectoryName = ".ignixa";

    private static readonly HashSet<string> NdjsonExtensions = new(StringComparer.OrdinalIgnoreCase) { ".ndjson", ".jsonl" };

    /// <summary>Walks <paramref name="rootPath"/> and locates every resource in it.</summary>
    /// <param name="rootPath">Folder to catalogue.</param>
    /// <param name="validResourceTypes">
    /// Resource type names valid for the FHIR version in use. Entries of any other type are not
    /// catalogued and produce an <c>unknown-resource-type</c> diagnostic.
    /// </param>
    /// <param name="cancellationToken">Cancels the walk between files.</param>
    public static FolderCatalog Build(string rootPath, IReadOnlySet<string> validResourceTypes, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(validResourceTypes);
        string root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Folder '{root}' does not exist.");
        }

        var state = new BuildState(root, validResourceTypes);
        foreach (string file in EnumerateFiles(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string extension = Path.GetExtension(file);
            if (NdjsonExtensions.Contains(extension))
            {
                state.FilesScanned++;
                ReadNdjson(file, state);
            }
            else if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
            {
                state.FilesScanned++;
                ReadJson(file, state);
            }
            else
            {
                state.FilesSkipped++;
            }
        }

        return new FolderCatalog(root, state.Entries, state.Diagnostics, state.FilesScanned, state.FilesSkipped, state.ContainerBundles, state.Files, DateTimeOffset.UtcNow, state.FullUrls, state.Duplicates);
    }

    /// <summary>
    /// Counts the files that differ from what <paramref name="catalog"/> recorded when it was built: scanned files
    /// that were modified or removed, and files with a supported extension that have appeared since. Costs one
    /// directory walk and one stat per file; nothing is read.
    /// </summary>
    /// <exception cref="IOException">The folder or a subfolder can no longer be listed (deleted, unmounted); also <see cref="UnauthorizedAccessException"/> when it is no longer readable.</exception>
    public static int CountStaleFiles(FolderCatalog catalog, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        Dictionary<string, CatalogFile> recorded = catalog.Files.ToDictionary(file => file.Path, StringComparer.Ordinal);
        int stale = 0;
        int seenRecorded = 0;
        foreach (string file in EnumerateFiles(catalog.RootPath, ordered: false).Where(IsScanned))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(file);
            if (!info.Exists)
            {
                continue;
            }

            string relativePath = Path.GetRelativePath(catalog.RootPath, file).Replace('\\', '/');
            if (!recorded.TryGetValue(relativePath, out CatalogFile? before))
            {
                stale++;
                continue;
            }

            seenRecorded++;
            if (before.Length != info.Length || before.LastWriteTimeUtcTicks != info.LastWriteTimeUtc.Ticks)
            {
                stale++;
            }
        }

        return stale + (recorded.Count - seenRecorded);
    }

    private static bool IsScanned(string file)
    {
        string extension = Path.GetExtension(file);
        return NdjsonExtensions.Contains(extension) || string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase);
    }

    // Cataloguing needs the deterministic order; counting changes does not, so it skips the sort.
    private static IEnumerable<string> EnumerateFiles(string root, bool ordered = true)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        string cacheDirectory = Path.Combine(root, CacheDirectoryName) + Path.DirectorySeparatorChar;
        IEnumerable<string> files = Directory.EnumerateFiles(root, "*", options)
            .Where(file => !file.StartsWith(cacheDirectory, StringComparison.Ordinal));
        return ordered ? files.Order(StringComparer.Ordinal) : files;
    }

    private static void ReadJson(string file, BuildState state)
    {
        string relativePath = state.RecordFile(file);
        byte[] bytes = File.ReadAllBytes(file);
        try
        {
            var (resourceType, _) = ResourceSniffer.Sniff(bytes);
            if (resourceType == "Bundle")
            {
                state.ContainerBundles++;
                IReadOnlyList<(int Offset, int Length, string? FullUrl)> ranges = ResourceSniffer.LocateBundleEntries(bytes);
                for (int i = 0; i < ranges.Count; i++)
                {
                    var (offset, length, fullUrl) = ranges[i];
                    state.Add(relativePath, bytes.AsSpan(offset, length), offset, line: null, ordinal: i, fullUrl);
                }

                return;
            }

            state.Add(relativePath, bytes, 0, line: null, ordinal: 0);
        }
        catch (JsonException ex)
        {
            state.Diagnostics.Add(new CatalogDiagnostic("invalid-json", relativePath, null, ex.Message));
        }
    }

    private static void ReadNdjson(string file, BuildState state)
    {
        string relativePath = state.RecordFile(file);
        byte[] bytes = File.ReadAllBytes(file);
        int lineStart = 0;
        int lineNumber = 0;
        while (lineStart < bytes.Length)
        {
            int newline = Array.IndexOf(bytes, (byte)'\n', lineStart);
            int lineEnd = newline < 0 ? bytes.Length : newline;
            lineNumber++;

            ReadOnlySpan<byte> line = bytes.AsSpan(lineStart, lineEnd - lineStart).TrimEnd((byte)'\r');
            if (!line.Trim(" \t"u8).IsEmpty)
            {
                try
                {
                    state.Add(relativePath, line, lineStart, lineNumber, ordinal: lineNumber);
                }
                catch (JsonException ex)
                {
                    state.Diagnostics.Add(new CatalogDiagnostic("invalid-json", relativePath, lineNumber, ex.Message));
                }
            }

            lineStart = lineEnd + 1;
        }
    }

    private sealed class BuildState(string root, IReadOnlySet<string> validResourceTypes)
    {
        private readonly Dictionary<(string ResourceType, string Id), CatalogEntry> _seen = [];

        public List<CatalogEntry> Entries { get; } = [];

        public List<CatalogDiagnostic> Diagnostics { get; } = [];

        public List<CatalogFile> Files { get; } = [];

        public List<BundleFullUrl> FullUrls { get; } = [];

        public List<CatalogDuplicate> Duplicates { get; } = [];

        public int FilesScanned { get; set; }

        public int FilesSkipped { get; set; }

        public int ContainerBundles { get; set; }

        public string Relative(string file) => Path.GetRelativePath(root, file).Replace('\\', '/');

        /// <summary>
        /// Records the file's size and last-write time and returns its relative path. Called before the file is
        /// read, so a write racing the read can only make the recorded stamp older than the content, never newer.
        /// </summary>
        public string RecordFile(string file)
        {
            var info = new FileInfo(file);
            string relativePath = Relative(file);
            Files.Add(new CatalogFile(relativePath, info.Length, info.LastWriteTimeUtc.Ticks));
            return relativePath;
        }

        public void Add(string relativePath, ReadOnlySpan<byte> json, long offset, int? line, int ordinal, string? fullUrl = null)
        {
            var (resourceType, id) = ResourceSniffer.Sniff(json);
            if (string.IsNullOrEmpty(resourceType))
            {
                Diagnostics.Add(new CatalogDiagnostic("not-a-resource", relativePath, line, "JSON object has no resourceType."));
                return;
            }

            if (!validResourceTypes.Contains(resourceType))
            {
                Diagnostics.Add(new CatalogDiagnostic("unknown-resource-type", relativePath, line, $"'{resourceType}' is not a resource type of the selected FHIR version."));
                return;
            }

            bool synthetic = string.IsNullOrEmpty(id);
            string resourceId = synthetic ? FullUrlId(fullUrl, resourceType) ?? SyntheticId(relativePath, ordinal) : id!;
            var entry = new CatalogEntry(resourceType, resourceId, relativePath, offset, json.Length, line, synthetic, fullUrl);
            if (fullUrl is not null)
            {
                // Recorded for a duplicate too: its fullUrl still names this Type/id, which the first occurrence holds.
                FullUrls.Add(new BundleFullUrl(fullUrl, relativePath, resourceType, resourceId));
            }

            if (_seen.TryGetValue((resourceType, resourceId), out CatalogEntry? first))
            {
                Duplicates.Add(new CatalogDuplicate(resourceType, resourceId, Describe(first), Describe(entry)));
                Diagnostics.Add(new CatalogDiagnostic(
                    "duplicate-id",
                    relativePath,
                    line,
                    $"{FhirIds.Reference(resourceType, resourceId)} also appears in {Describe(first)}; the first occurrence is used."));
                return;
            }

            _seen.Add((resourceType, resourceId), entry);
            Entries.Add(entry);
            if (!synthetic && !FhirIds.IsValid(resourceId))
            {
                // The id itself is never echoed: it is folder text that is not shaped like an id.
                Diagnostics.Add(new CatalogDiagnostic(
                    FhirIds.InvalidCode,
                    relativePath,
                    line,
                    $"A {resourceType} resource has an id that is not a FHIR id (1 to 64 letters, digits, '-' or '.'); every tool names it {FhirIds.Reference(resourceType, resourceId)}, which get, search (_id, compartment) and the patient tools accept, and returns its JSON as written."));
            }
        }

        private static string Describe(CatalogEntry entry) =>
            entry.Line is int line ? $"{entry.FilePath}:{line}" : $"{entry.FilePath}@{entry.Offset}";

        /// <summary>The id a RESTful fullUrl gives an entry of <paramref name="resourceType"/>, or null when it gives none.</summary>
        private static string? FullUrlId(string? fullUrl, string resourceType) =>
            fullUrl is not null && RestfulUrl.TryParse(fullUrl, out _, out string type, out string id) && type == resourceType ? id : null;

        private static string SyntheticId(string relativePath, int ordinal)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{relativePath}#{ordinal}"));
            return "synthetic-" + Convert.ToHexStringLower(hash.AsSpan(0, 12));
        }
    }
}
