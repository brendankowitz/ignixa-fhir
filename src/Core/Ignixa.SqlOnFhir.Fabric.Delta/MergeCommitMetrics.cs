// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json;

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>
/// The row counts of a committed MERGE, read from its commit's <c>commitInfo.operationMetrics</c>. The binding's
/// merge call does not return the engine's metrics, and the commit file is immutable once published, so it is the
/// authoritative record of what the merge did.
/// </summary>
internal sealed record MergeCommitMetrics(long InsertedRows, long UpdatedRows)
{
    private const string MergeOperation = "MERGE";

    /// <exception cref="InvalidDataException">
    /// The commit has no MERGE commit info with the row count metrics, or the metrics count more inserted and updated
    /// rows than the <paramref name="sourceRows"/> the merge was given (one source row updates every target row sharing
    /// its id, so a target holding duplicate ids can cause this after a real commit).
    /// </exception>
    public static async Task<MergeCommitMetrics> ReadAsync(string tablePath, long version, long sourceRows, CancellationToken cancellationToken)
    {
        var commitPath = Path.Combine(
            tablePath,
            FabricSilverDeltaTables.LogDirectoryName,
            version.ToString("D20", CultureInfo.InvariantCulture) + ".json");

        foreach (var line in await File.ReadAllLinesAsync(commitPath, cancellationToken))
        {
            using var action = JsonDocument.Parse(line);
            if (action.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Missing(version, "has a line that is not a JSON object");
            }

            if (action.RootElement.TryGetProperty("commitInfo", out var commitInfo))
            {
                var metrics = FromCommitInfo(commitInfo, version);

                // Compared without adding, so counts near long.MaxValue cannot wrap past the bound.
                return metrics.InsertedRows <= sourceRows && metrics.UpdatedRows <= sourceRows - metrics.InsertedRows
                    ? metrics
                    : throw Missing(version, $"counts more inserted and updated rows than the {sourceRows} merged; the table may hold duplicate ids");
            }
        }

        throw Missing(version, "has no commit info");
    }

    private static MergeCommitMetrics FromCommitInfo(JsonElement commitInfo, long version)
    {
        if (commitInfo.ValueKind != JsonValueKind.Object)
        {
            throw Missing(version, "has commit info that is not a JSON object");
        }

        if (!commitInfo.TryGetProperty("operation", out var operation) || operation.ValueKind != JsonValueKind.String || operation.GetString() != MergeOperation)
        {
            throw Missing(version, "is not a MERGE");
        }

        if (!commitInfo.TryGetProperty("operationMetrics", out var metrics) || metrics.ValueKind != JsonValueKind.Object)
        {
            throw Missing(version, "has no operation metrics");
        }

        return new MergeCommitMetrics(
            Count(metrics, "num_target_rows_inserted", version),
            Count(metrics, "num_target_rows_updated", version));
    }

    private static long Count(JsonElement metrics, string name, long version) =>
        metrics.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var count) && count >= 0
            ? count
            : throw Missing(version, $"has no {name} metric");

    private static InvalidDataException Missing(long version, string problem) =>
        new($"The commit of table version {version} {problem}.");
}
