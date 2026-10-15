// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Reading;

/// <summary>
/// Reads a catalogued resource's raw JSON by seeking to its recorded byte range, without reading or parsing the
/// rest of its file. Because the file may have changed since the catalog was built, the range is checked to still
/// hold the catalogued resource; if not, the read reports a problem instead of returning another resource.
/// </summary>
public static class ResourceReader
{
    public static ResourceReadResult Read(string rootPath, CatalogEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(entry);

        byte[] json = new byte[entry.Length];
        try
        {
            using var stream = new FileStream(Path.Combine(rootPath, entry.FilePath), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(entry.Offset, SeekOrigin.Begin);
            stream.ReadExactly(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Problem(entry, "resource-unreadable", $"{Describe(entry)} could not be read from its file: {ex.Message}");
        }

        try
        {
            (string? foundType, string? foundId) = ResourceSniffer.Sniff(json);
            if (!string.Equals(foundType, entry.ResourceType, StringComparison.Ordinal)
                || (!entry.HasSyntheticId && !string.Equals(foundId, entry.Id, StringComparison.Ordinal)))
            {
                return Problem(entry, "catalog-stale", $"{Describe(entry)} is no longer where the catalog recorded it (found {foundType ?? "(no resourceType)"}/{foundId ?? "(no id)"}); the file changed after it was catalogued.");
            }

            return JsonNode.Parse(json) is JsonObject resource
                ? new ResourceReadResult(resource, Problem: null)
                : Problem(entry, "invalid-resource", $"{Describe(entry)} is not a JSON object.");
        }
        catch (JsonException ex)
        {
            return Problem(entry, "invalid-resource", $"{Describe(entry)}: {ex.Message}");
        }
    }

    public static string Describe(CatalogEntry entry) => $"{entry.ResourceType}/{entry.Id}";

    private static ResourceReadResult Problem(CatalogEntry entry, string code, string diagnostics) => new(
        Resource: null,
        new IssueItem("error", code, diagnostics, entry.Line is int line ? $"{entry.FilePath}:{line}" : entry.FilePath));
}
