// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.BulkDelete.Models;

/// <summary>
/// Input for one <see cref="Activities.BulkDeleteBatchActivity"/> page.
/// </summary>
/// <param name="JobId">Job ID.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="ResourceType">Type searched by this batch.</param>
/// <param name="SearchQuery">Encoded search filters.</param>
/// <param name="Mode">Physical effect of the deletion.</param>
/// <param name="ExcludedResourceTypes">Types dropped from the include cascade (ordinal-ignore-case).</param>
/// <param name="RemoveReferences">Rewrite referrers of deleted resources (hard delete only).</param>
/// <param name="BatchSize">Matches per page.</param>
/// <param name="ContinuationToken">
/// Purge-history cursor from the previous batch; always null for soft and hard delete, which re-read the
/// first page because deleted matches drop out of it.
/// </param>
/// <param name="CumulativeCounts">Deleted counts of all earlier batches, persisted together with this batch's.</param>
public record BulkDeleteBatchInput(
    string JobId,
    int TenantId,
    string ResourceType,
    string SearchQuery,
    BulkDeleteMode Mode,
    IReadOnlyList<string> ExcludedResourceTypes,
    bool RemoveReferences,
    int BatchSize,
    string? ContinuationToken,
    Dictionary<string, long> CumulativeCounts);
