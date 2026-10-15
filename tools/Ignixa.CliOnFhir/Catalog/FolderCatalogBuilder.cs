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
    /// <summary>Directory the tool reserves for its own cache; never catalogued.</summary>
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

        return new FolderCatalog(root, state.Entries, state.Diagnostics, state.FilesScanned, state.FilesSkipped, state.ContainerBundles, DateTimeOffset.UtcNow);
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        string cacheDirectory = Path.Combine(root, CacheDirectoryName) + Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(root, "*", options)
            .Where(file => !file.StartsWith(cacheDirectory, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
    }

    private static void ReadJson(string file, BuildState state)
    {
        byte[] bytes = File.ReadAllBytes(file);
        string relativePath = state.Relative(file);
        try
        {
            var (resourceType, _) = ResourceSniffer.Sniff(bytes);
            if (resourceType == "Bundle")
            {
                state.ContainerBundles++;
                IReadOnlyList<(int Offset, int Length)> ranges = ResourceSniffer.LocateBundleEntries(bytes);
                for (int i = 0; i < ranges.Count; i++)
                {
                    var (offset, length) = ranges[i];
                    state.Add(relativePath, bytes.AsSpan(offset, length), offset, line: null, ordinal: i);
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
        byte[] bytes = File.ReadAllBytes(file);
        string relativePath = state.Relative(file);
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

        public int FilesScanned { get; set; }

        public int FilesSkipped { get; set; }

        public int ContainerBundles { get; set; }

        public string Relative(string file) => Path.GetRelativePath(root, file).Replace('\\', '/');

        public void Add(string relativePath, ReadOnlySpan<byte> json, long offset, int? line, int ordinal)
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
            string resourceId = synthetic ? SyntheticId(relativePath, ordinal) : id!;
            var entry = new CatalogEntry(resourceType, resourceId, relativePath, offset, json.Length, line, synthetic);

            if (_seen.TryGetValue((resourceType, resourceId), out CatalogEntry? first))
            {
                Diagnostics.Add(new CatalogDiagnostic(
                    "duplicate-id",
                    relativePath,
                    line,
                    $"{resourceType}/{resourceId} also appears in {Describe(first)}; the first occurrence is used."));
                return;
            }

            _seen.Add((resourceType, resourceId), entry);
            Entries.Add(entry);
        }

        private static string Describe(CatalogEntry entry) =>
            entry.Line is int line ? $"{entry.FilePath}:{line}" : $"{entry.FilePath}@{entry.Offset}";

        private static string SyntheticId(string relativePath, int ordinal)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{relativePath}#{ordinal}"));
            return "synthetic-" + Convert.ToHexStringLower(hash.AsSpan(0, 12));
        }
    }
}
