// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>
/// The DataFusion settings every writer engine uses, fixed because they are what keeps a MERGE inside the memory
/// budget. Probe measurements of peak private bytes on a 50,000-row table with a 5,000-row batch: the default (one
/// partition per core, 28 on the probe host) about 11,000 MiB; one partition about 3,500 MiB in 23 s; one partition
/// with 1,024-row batches about 2,800 MiB in 18 s. A 100,000-row table at one partition peaked at about 5,400 MiB,
/// which is why the target-row ceiling exists; a 50-row merge into the same 50,000 rows split into 100 small files
/// peaked at about 4,600 MiB, which is why the target-file ceiling exists. That run varied both the source size and
/// the rewrite pattern against the 5,000-row baseline, so it does not isolate the effect of the file count. A DataFusion
/// spill pool is deliberately not configured: with one (only a 1 GiB pool was tried), the MERGE failed instead of
/// spilling. Upstream tracks MERGE memory in delta-rs issues
/// 2802 and 3786.
/// </summary>
internal static class MergeEngineSettings
{
    public const int TargetPartitions = 1;

    public const int BatchSize = 1024;
}
