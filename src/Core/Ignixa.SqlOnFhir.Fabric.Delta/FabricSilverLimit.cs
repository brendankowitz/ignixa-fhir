// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>A fail-closed ceiling of <see cref="FabricSilverWriterOptions"/>.</summary>
public enum FabricSilverLimit
{
    /// <summary>A table's batch holds more resources than <see cref="FabricSilverWriterOptions.MaxSourceResourcesPerTable"/>.</summary>
    SourceResources,

    /// <summary>A target table holds more rows than <see cref="FabricSilverWriterOptions.MaxTargetRows"/>.</summary>
    TargetRows,

    /// <summary>A target table's row count could not be determined, so the row ceiling cannot be proven.</summary>
    TargetRowsUnknown,

    /// <summary>
    /// A target table's current snapshot holds more data files than <see cref="FabricSilverWriterOptions.MaxTargetFiles"/>;
    /// it must be compacted before the writer will merge into it.
    /// </summary>
    TargetFiles,

    /// <summary>A target table's data file count could not be determined, so the file ceiling cannot be proven.</summary>
    TargetFilesUnknown,
}
