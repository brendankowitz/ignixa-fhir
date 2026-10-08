// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Models;

/// <summary>
/// Result data for a completed/cancelled/failed bulk-delete job, stored as JSON in BackgroundJob.Result.
/// </summary>
public class BulkDeleteJobResult
{
    /// <summary>
    /// Total resources deleted, keyed by resource type.
    /// </summary>
    public Dictionary<string, long> ResourceDeletedCount { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Failure diagnostics (e.g. the error that stopped the job, or why a batch could not proceed).
    /// Empty for a job that completed or was cancelled without error.
    /// </summary>
    public IReadOnlyList<string> Issues { get; init; } = [];
}
