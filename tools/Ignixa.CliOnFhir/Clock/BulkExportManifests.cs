// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Terminology;

namespace Ignixa.CliOnFhir.Clock;

/// <summary>
/// Finds the data-pull time a Bulk Data export recorded: the <c>transactionTime</c> of its complete-status manifest (Bulk Data
/// Access IG, "Response - Complete Status": a JSON object with <c>transactionTime</c>, an instant, and an <c>output</c> array).
/// The specification gives the manifest no file name, so only the files directly in the folder (not in a subfolder) whose name
/// ends in <c>.json</c> and contains <c>manifest</c> (any case) are read, each at most <see cref="MaxBytes"/>. A candidate that is
/// not a manifest is named in an information issue; when manifests disagree, the latest time is used and the spread is noted.
/// </summary>
internal static class BulkExportManifests
{
    /// <summary>The largest candidate read; a manifest lists files, so even a large export's is far smaller.</summary>
    public const int MaxBytes = 1024 * 1024;

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 64, CommentHandling = JsonCommentHandling.Disallow };

    /// <summary>The latest manifest <c>transactionTime</c> at the root of <paramref name="rootPath"/>, or null; and the notes.</summary>
    public static (DateTimeOffset? TransactionTime, List<IssueItem> Issues) Detect(string rootPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);

        List<IssueItem> issues = [];
        List<DateTimeOffset> found = [];
        foreach (string file in Candidates(rootPath))
        {
            (DateTimeOffset? time, string? problem) = Read(file);
            if (time is { } transactionTime)
            {
                found.Add(transactionTime);
                continue;
            }

            string name = TerminologyNames.Path(Path.GetFileName(file));
            issues.Add(new IssueItem("information", "as-of-manifest-ignored", $"The file {name}, named like a Bulk Data export manifest, was not used for asOf: {problem}.", name));
        }

        if (found.Count == 0)
        {
            return (null, issues);
        }

        DateTimeOffset latest = found.MaxBy(time => time.UtcDateTime);
        if (found.Select(time => time.UtcDateTime).Distinct().Count() > 1)
        {
            issues.Add(new IssueItem(
                "information",
                "as-of-manifest-spread",
                $"{found.Count} Bulk Data export manifests give different transaction times, from {FhirInstant.Format(found.MinBy(time => time.UtcDateTime))} to {FhirInstant.Format(latest)}; asOf is the latest.",
                Location: null));
        }

        return (latest, issues);
    }

    private static List<string> Candidates(string rootPath)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        try
        {
            return [.. Directory.EnumerateFiles(rootPath, "*", options)
                .Where(file => Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileName(file).Contains("manifest", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // The manifest's transactionTime, or why the file is not a manifest (in the tool's words: nothing of the file is quoted).
    private static (DateTimeOffset? Time, string? Problem) Read(string file)
    {
        byte[] bytes;
        try
        {
            bytes = FileRegion.Read(file, 0, MaxBytes, out bool truncated);
            if (truncated)
            {
                return (null, $"it is larger than {MaxBytes} bytes");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, $"it could not be read ({ex.GetType().Name})");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes, Options);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("output", out JsonElement output) || output.ValueKind != JsonValueKind.Array)
            {
                return (null, "it is not a JSON object with an output array");
            }

            if (!root.TryGetProperty("transactionTime", out JsonElement time) || time.ValueKind != JsonValueKind.String)
            {
                return (null, "it has no transactionTime");
            }

            if (!FhirInstant.TryParse(time.GetString(), out DateTimeOffset value))
            {
                return (null, "its transactionTime is not a FHIR instant");
            }

            return (value, null);
        }
        catch (JsonException)
        {
            return (null, "it is not valid JSON");
        }
    }
}
