// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>What one call of the writer did, table by table.</summary>
public sealed class FabricSilverBatchResult
{
    internal FabricSilverBatchResult(IReadOnlyList<FabricSilverTableResult> tables)
    {
        Tables = tables;
    }

    /// <summary>One result per table batch, in the order the batches were given.</summary>
    public IReadOnlyList<FabricSilverTableResult> Tables { get; }
}
