// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Apache.Arrow;
using DeltaLake.Interfaces;
using DeltaLake.Table;

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>
/// Proves a target table is within the writer's size ceilings before anything is written, failing closed when a
/// size cannot be determined. Both counts read the snapshot the table handle loaded.
/// </summary>
internal static class TargetTableSize
{
    private const string RowCountQuery = "SELECT COUNT(*) AS row_count FROM deltatable";

    public static async Task RequireWithinAsync(ITable table, string tablePath, FabricSilverWriterOptions options, CancellationToken cancellationToken)
    {
        // The file count comes from the log alone, so it is checked first and refuses a fragmented table cheaply.
        RequireFiles(await CountLiveFilesAsync(tablePath, table.Version(), options.MaxTargetFiles, cancellationToken), tablePath, options.MaxTargetFiles);

        var batches = new List<RecordBatch>();
        try
        {
            await foreach (var batch in table.QueryAsync(new SelectQuery(RowCountQuery), cancellationToken))
            {
                batches.Add(batch);
            }

            RequireRows(batches, tablePath, options.MaxTargetRows);
        }
        finally
        {
            batches.ForEach(batch => batch.Dispose());
        }
    }

    /// <summary>Requires the live data files of the snapshot to number at most <paramref name="limit"/>.</summary>
    public static void RequireFiles(int liveFiles, string tablePath, int limit)
    {
        if (liveFiles > limit)
        {
            throw new FabricSilverLimitExceededException(tablePath, FabricSilverLimit.TargetFiles, limit, liveFiles);
        }
    }

    /// <summary>
    /// Counts the live data files of <paramref name="loadedVersion"/>, the version the table handle loaded, so both
    /// ceilings measure the same snapshot. Fails closed with <see cref="FabricSilverLimit.TargetFilesUnknown"/> when
    /// there is no loaded version or the log cannot be replayed.
    /// </summary>
    public static async Task<int> CountLiveFilesAsync(string tablePath, ulong? loadedVersion, int limit, CancellationToken cancellationToken)
    {
        if (loadedVersion is not { } version)
        {
            throw new FabricSilverLimitExceededException(tablePath, FabricSilverLimit.TargetFilesUnknown, limit, actual: null);
        }

        try
        {
            return await LiveDataFiles.CountAsync(tablePath, checked((long)version), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new FabricSilverLimitExceededException(tablePath, FabricSilverLimit.TargetFilesUnknown, limit, actual: null, exception);
        }
    }

    /// <summary>
    /// Requires the result of the row count query to be exactly one non-null <c>long</c>, at most
    /// <paramref name="limit"/>.
    /// </summary>
    public static void RequireRows(IReadOnlyList<RecordBatch> batches, string tablePath, long limit)
    {
        var rows = batches.Where(batch => batch.Length > 0).ToList();
        if (rows.Count != 1 ||
            rows[0].Length != 1 ||
            rows[0].ColumnCount != 1 ||
            rows[0].Column(0) is not Int64Array counts ||
            counts.GetValue(0) is not { } count)
        {
            throw new FabricSilverLimitExceededException(tablePath, FabricSilverLimit.TargetRowsUnknown, limit, actual: null);
        }

        if (count > limit)
        {
            throw new FabricSilverLimitExceededException(tablePath, FabricSilverLimit.TargetRows, limit, count);
        }
    }
}
