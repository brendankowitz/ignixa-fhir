// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Parquet;
using Parquet.Schema;

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>
/// Counts the live data files of one table version by replaying its Delta log, read-only: the newest complete
/// checkpoint at or below the version, then every JSON commit after it.
/// </summary>
/// <remarks>
/// <para>
/// The binding's <c>ITable.FilesAsync</c> and <c>ITable.FileUrisAsync</c> must not be used: in DeltaLake.Net 0.33.3
/// the native bridge drops the path array it hands out and frees it again when the managed side releases it. That
/// double free corrupts the process heap once a table has more than one file.
/// </para>
/// <para>
/// The validator pins the protocol at reader 1 / writer 4, so the only checkpoints a valid table can have are classic
/// single-part and multi-part ones. Any other checkpoint is ignored, which only makes the replay start earlier; a commit
/// missing from the replayed range fails the count rather than leaving it partial.
/// </para>
/// </remarks>
internal static partial class LiveDataFiles
{
    private const string AddAction = "add";
    private const string RemoveAction = "remove";
    private const string PathProperty = "path";

    /// <exception cref="IOException">The log or a checkpoint cannot be read, or a commit in the range is missing.</exception>
    /// <exception cref="InvalidDataException">An action or checkpoint does not have the shape the Delta protocol defines.</exception>
    /// <exception cref="JsonException">A commit line is not JSON.</exception>
    public static async Task<int> CountAsync(string tablePath, long version, CancellationToken cancellationToken)
    {
        var logDirectory = Path.Combine(tablePath, FabricSilverDeltaTables.LogDirectoryName);
        var live = new HashSet<string>(StringComparer.Ordinal);

        var checkpoint = NewestCompleteCheckpoint(Directory.GetFiles(logDirectory).Select(Path.GetFileName).OfType<string>(), version);
        foreach (var part in checkpoint?.Parts ?? [])
        {
            await AddCheckpointFilesAsync(Path.Combine(logDirectory, part), live, cancellationToken);
        }

        for (var commit = (checkpoint?.Version ?? -1) + 1; commit <= version; commit++)
        {
            var commitPath = Path.Combine(logDirectory, commit.ToString("D20", CultureInfo.InvariantCulture) + ".json");
            foreach (var line in await File.ReadAllLinesAsync(commitPath, cancellationToken))
            {
                Apply(line, live);
            }
        }

        return live.Count;
    }

    private static (long Version, string[] Parts)? NewestCompleteCheckpoint(IEnumerable<string> logEntries, long version)
    {
        var candidates = new Dictionary<(long Version, int PartCount), SortedDictionary<int, string>>();
        foreach (var entry in logEntries)
        {
            var match = CheckpointName().Match(entry);
            if (!match.Success)
            {
                continue;
            }

            var checkpointVersion = long.Parse(match.Groups["version"].Value, CultureInfo.InvariantCulture);
            var (part, partCount) = match.Groups["part"].Success
                ? (int.Parse(match.Groups["part"].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups["parts"].Value, CultureInfo.InvariantCulture))
                : (1, 1);
            if (checkpointVersion > version || part < 1 || part > partCount)
            {
                continue;
            }

            if (!candidates.TryGetValue((checkpointVersion, partCount), out var parts))
            {
                candidates[(checkpointVersion, partCount)] = parts = [];
            }

            parts[part] = entry;
        }

        // A multi-part checkpoint is published part by part, so one with a missing part is still being written.
        var complete = candidates
            .Where(candidate => candidate.Value.Count == candidate.Key.PartCount)
            .OrderByDescending(candidate => candidate.Key.Version)
            .ToList();
        return complete.Count == 0 ? null : (complete[0].Key.Version, [.. complete[0].Value.Values]);
    }

    // A checkpoint row whose add action is set is a live file; every other action in it is not.
    private static async Task AddCheckpointFilesAsync(string checkpointPath, HashSet<string> live, CancellationToken cancellationToken)
    {
        using var reader = await ParquetReader.CreateAsync(checkpointPath, cancellationToken: cancellationToken);
        var add = reader.Schema.Fields.OfType<StructField>().SingleOrDefault(field => field.Name == AddAction)
            ?? throw new InvalidDataException("A checkpoint has no add column.");
        var path = add.Fields.OfType<DataField>().SingleOrDefault(field => field.Name == PathProperty)
            ?? throw new InvalidDataException("A checkpoint's add column has no path.");

        for (var group = 0; group < reader.RowGroupCount; group++)
        {
            using var rowGroup = reader.OpenRowGroupReader(group);
            var column = await rowGroup.ReadColumnAsync(path, cancellationToken);
            foreach (var value in column.Data)
            {
                if (value is string filePath)
                {
                    live.Add(filePath);
                }
            }
        }
    }

    private static void Apply(string line, HashSet<string> live)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        using var action = JsonDocument.Parse(line);
        if (action.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("A commit line is not a JSON object.");
        }

        if (action.RootElement.TryGetProperty(AddAction, out var added))
        {
            live.Add(PathOf(added, AddAction));
        }
        else if (action.RootElement.TryGetProperty(RemoveAction, out var removed))
        {
            live.Remove(PathOf(removed, RemoveAction));
        }
    }

    private static string PathOf(JsonElement action, string actionName) =>
        action.ValueKind == JsonValueKind.Object &&
        action.TryGetProperty(PathProperty, out var path) &&
        path.ValueKind == JsonValueKind.String &&
        path.GetString() is { Length: > 0 } value
            ? value
            : throw new InvalidDataException($"A commit has an {actionName} action without a path.");

    [GeneratedRegex(@"^(?<version>\d{20})\.checkpoint(\.(?<part>\d{10})\.(?<parts>\d{10}))?\.parquet$", RegexOptions.CultureInvariant)]
    private static partial Regex CheckpointName();
}
