// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Apache.Arrow;
using DeltaLake.Table;

namespace Ignixa.SqlOnFhir.Fabric.Delta.Tests;

/// <summary>Reads a table back through the Delta engine, the second reader beside <see cref="DeltaParquetReader"/>.</summary>
internal static class EngineTableReader
{
    /// <summary>The version the engine loads and the sorted Silver ids it returns for the table.</summary>
    public static async Task<(long Version, IReadOnlyList<string> Ids)> ReadIdsAsync(string tablePath)
    {
        using var engine = new DeltaEngine(EngineOptions.Default);
        using var table = await engine.LoadTableAsync(new TableOptions { TableLocation = tablePath }, CancellationToken.None);
        var ids = new List<string>();

        // Every batch is drained and disposed: the binding's streams must never be left early.
        await foreach (var batch in table.QueryAsync(new SelectQuery("SELECT id FROM deltatable"), CancellationToken.None))
        {
            using (batch)
            {
                // DataFusion may hand strings back as either Arrow layout.
                IArrowArray column = batch.Column(0);
                for (var index = 0; index < column.Length; index++)
                {
                    ids.Add(column switch
                    {
                        StringViewArray view => view.GetString(index),
                        StringArray plain => plain.GetString(index),
                        _ => throw new InvalidOperationException($"The engine returned ids as {column.GetType().Name}."),
                    });
                }
            }
        }

        ids.Sort(StringComparer.Ordinal);
        return ((long)(table.Version() ?? throw new InvalidOperationException("The engine loaded no version.")), ids);
    }
}
