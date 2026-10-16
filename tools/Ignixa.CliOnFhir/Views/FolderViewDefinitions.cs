// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Views;

/// <summary>
/// The ViewDefinition resources in the folder. ViewDefinition is a resource type of none of the FHIR versions the tool reads
/// (it is an R6 resource), so the catalog does not hold one: it reports the resource as an <c>unknown-resource-type</c>
/// diagnostic. They are therefore found by reading, and only, the files and lines of those diagnostics, never by walking
/// the folder again.
/// </summary>
internal static class FolderViewDefinitions
{
    private const string UnknownResourceTypeCode = "unknown-resource-type";

    /// <summary>Every ViewDefinition among the files the catalog could not catalogue, in catalog order.</summary>
    /// <exception cref="ToolRequestException">A flagged file can no longer be read, or is no longer what the catalog saw.</exception>
    public static IReadOnlyList<FolderViewDefinition> Find(FolderSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        List<FolderViewDefinition> found = [];
        foreach (IGrouping<string, CatalogDiagnostic> file in session.Catalog.Diagnostics
            .Where(diagnostic => diagnostic.Code == UnknownResourceTypeCode)
            .GroupBy(diagnostic => diagnostic.FilePath, StringComparer.Ordinal))
        {
            byte[] bytes = Read(session, file.Key);
            try
            {
                found.AddRange(IsNdjson(file.Key)
                    ? InLines(file.Key, bytes, [.. file.Select(diagnostic => diagnostic.Line).OfType<int>()])
                    : InJson(file.Key, bytes));
            }
            catch (JsonException ex)
            {
                throw ToolRequestException.Error("view-unreadable", $"{file.Key} is no longer the JSON the catalog read: {ex.Message}");
            }
        }

        return found;
    }

    private static byte[] Read(FolderSession session, string relativePath)
    {
        try
        {
            return File.ReadAllBytes(Path.Combine(session.RootPath, relativePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw ToolRequestException.Error("view-unreadable", $"{relativePath} could not be read while looking for ViewDefinitions: {ex.Message}");
        }
    }

    private static bool IsNdjson(string path) =>
        path.EndsWith(".ndjson", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);

    // Lines are located as the catalog located them, so the offset is the same one evidence gives for any NDJSON resource.
    private static IEnumerable<FolderViewDefinition> InLines(string path, byte[] bytes, HashSet<int> flaggedLines)
    {
        int lineStart = 0;
        int lineNumber = 0;
        while (lineStart < bytes.Length)
        {
            int newline = Array.IndexOf(bytes, (byte)'\n', lineStart);
            int lineEnd = newline < 0 ? bytes.Length : newline;
            lineNumber++;
            if (flaggedLines.Contains(lineNumber) && Candidate(path, bytes.AsSpan(lineStart, lineEnd - lineStart).TrimEnd((byte)'\r'), lineStart, lineNumber) is { } view)
            {
                yield return view;
            }

            lineStart = lineEnd + 1;
        }
    }

    private static IEnumerable<FolderViewDefinition> InJson(string path, byte[] bytes)
    {
        if (ResourceSniffer.Sniff(bytes).ResourceType == "Bundle")
        {
            foreach ((int offset, int length, _) in ResourceSniffer.LocateBundleEntries(bytes))
            {
                if (Candidate(path, bytes.AsSpan(offset, length), offset, line: null) is { } entry)
                {
                    yield return entry;
                }
            }

            yield break;
        }

        if (Candidate(path, bytes, 0, line: null) is { } view)
        {
            yield return view;
        }
    }

    private static FolderViewDefinition? Candidate(string path, ReadOnlySpan<byte> json, int offset, int? line)
    {
        if (ResourceSniffer.Sniff(json).ResourceType != ViewDefinitions.ResourceType || JsonNode.Parse(json) is not JsonObject resource)
        {
            return null;
        }

        string? id = Text(resource, "id");
        string reference = id is null ? ViewDefinitions.ResourceType : FhirIds.Reference(ViewDefinitions.ResourceType, id);
        return new FolderViewDefinition(resource, id, Text(resource, "url"), Text(resource, "version"), new EvidenceItem(reference, path, line, offset));
    }

    private static string? Text(JsonObject resource, string name) =>
        resource[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;
}
