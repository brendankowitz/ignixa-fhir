// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.BulkDelete;

/// <summary>
/// Configuration for <c>$bulk-delete</c> jobs (section <see cref="SectionName"/>). Bound and validated at
/// host start; the batch size is snapshotted into each job's orchestration input, so changing it affects
/// only jobs started afterwards.
/// </summary>
public class BulkDeleteOptions
{
    public const string SectionName = "BulkDelete";

    public const int MinBatchSize = 1;

    public const int MaxBatchSize = 10_000;

    /// <summary>
    /// Matches searched and deleted per batch activity. Bounds the work lost to a cancel or crash and the
    /// <c>_include</c>/<c>_revinclude</c> fan-out of a single page. Default: 500.
    /// </summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>
    /// True when every option is within its documented range.
    /// </summary>
    public bool IsValid() => BatchSize is >= MinBatchSize and <= MaxBatchSize;
}
