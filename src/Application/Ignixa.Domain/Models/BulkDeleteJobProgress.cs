// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Models;

/// <summary>
/// Progress data for a bulk-delete job, stored as JSON in BackgroundJob.Progress. Persisted after
/// every batch so a status poll of a running or cancelled job can report partial counts.
/// </summary>
public class BulkDeleteJobProgress
{
    /// <summary>
    /// Cumulative resources deleted so far, keyed by resource type.
    /// </summary>
    public Dictionary<string, long> ResourceDeletedCount { get; init; } = new(StringComparer.Ordinal);
}
