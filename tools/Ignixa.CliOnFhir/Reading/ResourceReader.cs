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
        (byte[]? json, IssueItem? problem) = ReadBytes(rootPath, entry);
        if (json is null)
        {
            return new ResourceReadResult(Resource: null, problem);
        }

        try
        {
            return JsonNode.Parse(json) is JsonObject resource
                ? new ResourceReadResult(resource, Problem: null)
                : Problem(entry, "invalid-resource", $"{Describe(entry)} is not a JSON object.");
        }
        catch (JsonException ex)
        {
            return Problem(entry, "invalid-resource", $"{Describe(entry)}: {ex.Message}");
        }
    }

    /// <summary>
    /// The resource's JSON bytes, checked to still be the catalogued resource, or the problem that prevented it (an unreadable
    /// file, a changed file, malformed JSON at the recorded range); for a caller that parses the bytes itself.
    /// </summary>
    public static (byte[]? Json, IssueItem? Problem) ReadBytes(string rootPath, CatalogEntry entry)
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
            return (null, Unreadable(entry, ex));
        }

        return Check(json, entry) is { } problem ? (null, problem) : (json, null);
    }

    /// <summary>
    /// <see cref="ReadBytes(string, CatalogEntry)"/> over the whole of <paramref name="entry"/>'s file, already read, for a
    /// caller reading many resources of the same files.
    /// </summary>
    public static (ReadOnlyMemory<byte>? Json, IssueItem? Problem) Slice(byte[] file, CatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Offset + entry.Length > file.Length)
        {
            return (null, Problem(entry, "catalog-stale", $"{Describe(entry)} is no longer where the catalog recorded it (the file is now {file.Length} bytes, shorter than the recorded range); the file changed after it was catalogued.").Problem);
        }

        ReadOnlyMemory<byte> json = file.AsMemory((int)entry.Offset, entry.Length);
        return Check(json.Span, entry) is { } problem ? (null, problem) : (json, null);
    }

    /// <summary>The problem of a file that could not be read for <paramref name="entry"/>.</summary>
    public static IssueItem Unreadable(CatalogEntry entry, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(exception);

        return Problem(entry, "resource-unreadable", $"{Describe(entry)} could not be read from its file: {exception.Message}").Problem!;
    }

    private static IssueItem? Check(ReadOnlySpan<byte> json, CatalogEntry entry)
    {
        try
        {
            (string? foundType, string? foundId) = ResourceSniffer.Sniff(json);
            if (!string.Equals(foundType, entry.ResourceType, StringComparison.Ordinal)
                || (!entry.HasSyntheticId && !string.Equals(foundId, entry.Id, StringComparison.Ordinal)))
            {
                return Problem(entry, "catalog-stale", $"{Describe(entry)} is no longer where the catalog recorded it (found {foundType ?? "(no resourceType)"}/{foundId ?? "(no id)"}); the file changed after it was catalogued.").Problem;
            }
        }
        catch (JsonException ex)
        {
            return Problem(entry, "invalid-resource", $"{Describe(entry)}: {ex.Message}").Problem;
        }

        return null;
    }

    public static string Describe(CatalogEntry entry) => $"{entry.ResourceType}/{entry.Id}";

    private static ResourceReadResult Problem(CatalogEntry entry, string code, string diagnostics) => new(
        Resource: null,
        new IssueItem("error", code, diagnostics, entry.Line is int line ? $"{entry.FilePath}:{line}" : entry.FilePath));
}
