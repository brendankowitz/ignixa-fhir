// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Caching;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Reading;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Mapping;

/// <summary>
/// Finds the text of a map named by a call: inline, a file, or a StructureMap of the folder. Nothing is fetched: a url names a
/// StructureMap of the folder, never a place on the network.
/// </summary>
internal static class MapLocator
{
    /// <summary>The largest map read; a map is a few kilobytes, and parsing time grows with its length.</summary>
    public const int MaxBytes = 1024 * 1024;

    private const int MostNamed = 5;

    private const string StructureMapType = "StructureMap";

    private static readonly char[] FmlMarks = ['\n', '\r', '"'];

    /// <summary>The map <paramref name="map"/> names (see <see cref="MapRequest.Map"/>).</summary>
    /// <param name="folderMaps">The folder's StructureMaps, read once per run.</param>
    /// <exception cref="ToolRequestException"><c>map-not-found</c>, <c>map-ambiguous</c>, <c>map-unreadable</c> or <c>map-invalid</c> (too large).</exception>
    public static LoadedMap Resolve(FolderSession session, string map, Lazy<IReadOnlyList<FolderStructureMap>> folderMaps)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(map);
        ArgumentNullException.ThrowIfNull(folderMaps);

        string text = map.TrimStart('\uFEFF').Trim();
        if (text.StartsWith('{') || text.StartsWith('['))
        {
            return Inline(text, LoadedMap.StructureMap);
        }

        if (IsFml(text))
        {
            return Inline(text, LoadedMap.Fml);
        }

        if (Directory.Exists(text))
        {
            throw ToolRequestException.Error("map-unreadable", $"'{QueryEcho.Of(text)}' is a directory; name a map file.");
        }

        return File.Exists(text) ? FromFile(session, text) : FromFolder(text, folderMaps.Value);
    }

    /// <summary>The folder's StructureMaps that <paramref name="url"/> names: by url (any version), or, ending in <c>*</c>, by url prefix.</summary>
    public static IReadOnlyList<FolderStructureMap> ByUrl(IReadOnlyList<FolderStructureMap> folderMaps, string url)
    {
        ArgumentNullException.ThrowIfNull(folderMaps);
        ArgumentNullException.ThrowIfNull(url);

        return [.. folderMaps.Where(candidate => candidate.Url is not null && UrlMatches(url, candidate.Url))];
    }

    /// <summary>Whether <paramref name="url"/>, an <c>imports</c> url that may end in <c>*</c>, names a map whose url is <paramref name="candidate"/>.</summary>
    public static bool UrlMatches(string url, string candidate) =>
        url.EndsWith('*') ? candidate.StartsWith(url[..^1], StringComparison.Ordinal) : string.Equals(url, candidate, StringComparison.Ordinal);

    /// <summary>Every StructureMap the folder catalogued, in catalog order.</summary>
    /// <exception cref="ToolRequestException"><c>map-unreadable</c>: one can no longer be read as the catalog read it.</exception>
    public static IReadOnlyList<FolderStructureMap> FolderMaps(FolderSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        List<FolderStructureMap> found = [];
        foreach (CatalogEntry entry in session.Catalog.Entries.Where(entry => entry.ResourceType == StructureMapType))
        {
            if (entry.Length > MaxBytes)
            {
                continue; // never read: it is refused if it is named, as too large
            }

            (byte[]? json, IssueItem? problem) = ResourceReader.ReadBytes(session.RootPath, entry);
            if (json is null)
            {
                throw ToolRequestException.Error("map-unreadable", $"{ResourceReader.Describe(entry)} could not be read while looking for maps: {problem!.Code}.");
            }

            JsonObject resource;
            try
            {
                resource = JsonNode.Parse(json) as JsonObject ?? throw new JsonException();
            }
            catch (JsonException)
            {
                throw ToolRequestException.Error("map-unreadable", $"{ResourceReader.Describe(entry)} is no longer the JSON the catalog read.");
            }

            found.Add(new FolderStructureMap(entry, System.Text.Encoding.UTF8.GetString(json), Text(resource, "url"), Text(resource, "version")));
        }

        return found;
    }

    // FML has a line break, a quoted url or name, or at least starts with its keyword or a comment; a path, an id or a url has none
    // of them (as for a de-identification configuration, text that could not be a path is never taken for one).
    private static bool IsFml(string text) =>
        text.IndexOfAny(FmlMarks) >= 0 || text.StartsWith("map ", StringComparison.Ordinal) || text.StartsWith("//", StringComparison.Ordinal) || text.StartsWith("/*", StringComparison.Ordinal);

    private static LoadedMap Inline(string text, string format) =>
        text.Length <= MaxBytes
            ? new LoadedMap(text, format, LoadedMap.InlineSource, FromFolder: false, File: null, Reference: null, Evidence: null)
            : throw ToolRequestException.Error("map-invalid", $"The inline map is longer than {MaxBytes} characters.");

    private static LoadedMap FromFile(FolderSession session, string path)
    {
        string full = Path.GetFullPath(path);
        string text;
        try
        {
            if (new FileInfo(full).Length > MaxBytes)
            {
                throw ToolRequestException.Error("map-invalid", $"The map file '{QueryEcho.Of(full)}' is larger than {MaxBytes} bytes.");
            }

            text = File.ReadAllText(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw ToolRequestException.Error("map-unreadable", $"The map file '{QueryEcho.Of(full)}' could not be read ({ex.GetType().Name}).");
        }

        // A file inside the data folder is folder data like any other, whatever way it was named.
        bool insideFolder = new FolderOverlap(session.RootPath).Reason(full, "map file") is not null;
        string format = text.TrimStart().StartsWith('{') ? LoadedMap.StructureMap : LoadedMap.Fml;
        return new LoadedMap(text, format, LoadedMap.FileSource, insideFolder, full, Reference: null, Evidence: null);
    }

    private static LoadedMap FromFolder(string key, IReadOnlyList<FolderStructureMap> all)
    {
        List<FolderStructureMap> matches = [.. all.Where(candidate => Matches(candidate, key))];
        if (matches.Count == 0)
        {
            string held = all.Count == 0
                ? "the folder holds no StructureMap resources"
                : $"the folder holds {all.Count} StructureMap resources: {string.Join(", ", all.Take(MostNamed).Select(candidate => candidate.Reference))}{(all.Count > MostNamed ? ", ..." : string.Empty)}";
            throw ToolRequestException.Error("map-not-found", $"'{QueryEcho.Of(key)}' is not inline FML or StructureMap JSON, a file, or the id or url of a StructureMap in the folder ({held}); a map is never fetched from the network.");
        }

        if (matches.Count > 1)
        {
            throw ToolRequestException.Error("map-ambiguous", $"'{QueryEcho.Of(key)}' names {matches.Count} StructureMaps in the folder: {string.Join(", ", matches.Take(MostNamed).Select(candidate => candidate.Reference))}; name one by its id, or give url|version.");
        }

        return matches[0].Loaded;
    }

    // An id, StructureMap/id, a url (any version), or url|version.
    private static bool Matches(FolderStructureMap candidate, string key)
    {
        string id = candidate.Entry.Id;
        if (!candidate.Entry.HasSyntheticId && (id == key || $"{StructureMapType}/{id}" == key))
        {
            return true;
        }

        int bar = key.IndexOf('|', StringComparison.Ordinal);
        string url = bar < 0 ? key : key[..bar];
        return candidate.Url is not null && candidate.Url == url && (bar < 0 || candidate.Version == key[(bar + 1)..]);
    }

    private static string? Text(JsonObject resource, string name) =>
        resource[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;
}
