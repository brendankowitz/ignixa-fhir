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

    /// <exception cref="InvalidDataException">The commit has no MERGE commit info with the row count metrics.</exception>
    public static async Task<MergeCommitMetrics> ReadAsync(string tablePath, long version, CancellationToken cancellationToken)
    {
        var commitPath = Path.Combine(
            tablePath,
            FabricSilverDeltaTables.LogDirectoryName,
            version.ToString("D20", CultureInfo.InvariantCulture) + ".json");

        foreach (var line in await File.ReadAllLinesAsync(commitPath, cancellationToken))
        {
            using var action = JsonDocument.Parse(line);
            if (action.RootElement.TryGetProperty("commitInfo", out var commitInfo))
            {
                return FromCommitInfo(commitInfo, version);
            }
        }

        throw Missing(version, "has no commit info");
    }

    private static MergeCommitMetrics FromCommitInfo(JsonElement commitInfo, long version)
    {
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
