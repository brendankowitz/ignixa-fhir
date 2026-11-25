// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Ignixa.SqlOnFhir.Fabric.Delta.Tests;

/// <summary>
/// Replays a table's <c>_delta_log</c> JSON commits with System.Text.Json only, so tests can check what was written
/// without trusting the Delta engine that wrote or reads it.
/// </summary>
/// <remarks>
/// Deliberately strict and small: commits must be contiguous from version 0, every line must be one action object,
/// and checkpoints are not supported (a log that has one is rejected rather than half-read). Hidden temp files are
/// ignored as Delta readers do. Commits must be UTF-8 without a byte order mark; deletion vectors, partition values and a
/// <c>remove</c> of a file that is not live are rejected. Logged paths are URI-decoded. <see cref="DeltaParquetReader"/> reads the rows.
/// </remarks>
internal static class DeltaLogReader
{
    private const int VersionDigits = 20;

    public static DeltaLogSnapshot Read(string tableDirectory)
    {
        var logDirectory = Path.Combine(tableDirectory, "_delta_log");
        var commits = ListCommits(logDirectory);

        JsonElement? protocol = null;
        JsonElement? metaData = null;
        var liveFiles = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var removedFiles = new List<JsonElement>();
        var changeFiles = new List<DeltaLogChangeFile>();

        foreach (var (version, path) in commits)
        {
            foreach (var (name, action) in ReadActions(path))
            {
                switch (name)
                {
                    case "protocol":
                        protocol = action;
                        break;
                    case "metaData":
                        metaData = action;
                        break;
                    case "add":
                        RequireUnpartitionedWithoutDeletionVector(action);
                        liveFiles[PathOf(action)] = action;
                        break;
                    case "remove":
                        RequireUnpartitionedWithoutDeletionVector(action);
                        if (!liveFiles.Remove(PathOf(action)))
                        {
                            throw new InvalidDataException("A remove action names a file that is not live.");
                        }

                        removedFiles.Add(action);
                        break;
                    case "cdc":
                        RequireUnpartitionedWithoutDeletionVector(action);
                        changeFiles.Add(new DeltaLogChangeFile(version, action));
                        break;
                }
            }
        }

        var resolvedMetaData = metaData ?? throw new InvalidDataException("The log has no metaData action.");
        return new DeltaLogSnapshot(
            commits[^1].Version,
            protocol ?? throw new InvalidDataException("The log has no protocol action."),
            resolvedMetaData,
            [.. resolvedMetaData.GetProperty("configuration").EnumerateObject()
                .Select(property => KeyValuePair.Create(property.Name, property.Value.GetString()!))],
            liveFiles,
            removedFiles,
            changeFiles);
    }

    /// <summary>The raw lines of one commit, split on <c>\n</c> only.</summary>
    public static IReadOnlyList<string> ReadCommitLines(string tableDirectory, long version)
    {
        var path = Path.Combine(tableDirectory, "_delta_log", version.ToString("D20", CultureInfo.InvariantCulture) + ".json");
        return [.. new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(File.ReadAllBytes(path))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)];
    }

    private static List<(long Version, string Path)> ListCommits(string logDirectory)
    {
        var entries = Directory.EnumerateFiles(logDirectory)
            .Select(Path.GetFileName)
            .Where(name => !name!.StartsWith('.'))
            .ToList();

        if (entries.Any(name => name!.Contains(".checkpoint.", StringComparison.Ordinal)))
        {
            throw new NotSupportedException("The log has a checkpoint; this reader replays JSON commits only.");
        }

        var commits = entries
            .Where(name => name!.Length == VersionDigits + 5 && name.EndsWith(".json", StringComparison.Ordinal))
            .Select(name => (Version: long.Parse(name![..VersionDigits], CultureInfo.InvariantCulture), Path: Path.Combine(logDirectory, name)))
            .OrderBy(commit => commit.Version)
            .ToList();

        if (commits.Count == 0 || commits.Where((commit, index) => commit.Version != index).Any())
        {
            throw new InvalidDataException("The log's JSON commits are not contiguous from version 0.");
        }

        return commits;
    }

    private static IEnumerable<(string Name, JsonElement Action)> ReadActions(string path)
    {
        foreach (var line in DecodeStrict(File.ReadAllBytes(path)).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var document = JsonDocument.Parse(line);
            var properties = document.RootElement.EnumerateObject().ToList();
            if (properties.Count != 1)
            {
                throw new InvalidDataException("A log line is not a single action object.");
            }

            yield return (properties[0].Name, properties[0].Value.Clone());
        }
    }

    /// <summary>A logged path is a URI-encoded path relative to the table root; this returns it decoded.</summary>
    public static string PathOf(JsonElement action) => Uri.UnescapeDataString(action.GetProperty("path").GetString()!);

    // Delta writes commits as UTF-8 without a byte order mark; anything else is not a log this oracle trusts.
    private static string DecodeStrict(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
        {
            throw new InvalidDataException("A commit starts with a byte order mark.");
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("A commit is not valid UTF-8.", exception);
        }
    }

    // This oracle reads whole data files; deletion vectors and partition values would change which rows a file holds.
    private static void RequireUnpartitionedWithoutDeletionVector(JsonElement action)
    {
        if (action.TryGetProperty("deletionVector", out var deletionVector) && deletionVector.ValueKind != JsonValueKind.Null)
        {
            throw new NotSupportedException("A file action has a deletion vector; this reader does not apply them.");
        }

        if (action.TryGetProperty("partitionValues", out var partitionValues) &&
            partitionValues.ValueKind == JsonValueKind.Object &&
            partitionValues.EnumerateObject().Any())
        {
            throw new NotSupportedException("A file action has partition values; this reader supports unpartitioned tables only.");
        }
    }
}
