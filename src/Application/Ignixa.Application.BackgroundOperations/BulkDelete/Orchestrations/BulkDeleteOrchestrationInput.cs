// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.BulkDelete.Orchestrations;

/// <summary>
/// Input for <see cref="BulkDeleteOrchestration"/>. Snapshotted at kickoff so replay processes exactly the
/// same types, filters and batch size regardless of later schema or configuration changes.
/// </summary>
/// <param name="JobId">Job and orchestration instance ID.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="ResourceTypes">Types to process, in order.</param>
/// <param name="SearchQuery">Encoded search filters (no <c>_type</c>); parsed by each batch.</param>
/// <param name="Mode">Physical effect of the deletion.</param>
/// <param name="ExcludedResourceTypes">Types dropped from the include cascade.</param>
/// <param name="RemoveReferences">Rewrite referrers of deleted resources (hard delete only).</param>
/// <param name="BatchSize">Matches per batch activity.</param>
/// <param name="TypeIndex">
/// Index into <paramref name="ResourceTypes"/> to resume at. The fields from here on are carried across
/// <c>ContinueAsNew</c>; a kickoff input omits them, and their defaults start at the beginning.
/// </param>
/// <param name="ContinuationToken">Purge cursor within the resumed type; null in restart mode or at a type's start.</param>
/// <param name="PreviousFirstMatchKey">First match of the last batch, so the no-progress guard spans executions.</param>
/// <param name="CarriedCounts">Resources deleted by earlier executions, keyed by resource type.</param>
public record BulkDeleteOrchestrationInput(
    string JobId,
    int TenantId,
    IReadOnlyList<string> ResourceTypes,
    string SearchQuery,
    BulkDeleteMode Mode,
    IReadOnlyList<string> ExcludedResourceTypes,
    bool RemoveReferences,
    int BatchSize,
    int TypeIndex = 0,
    string? ContinuationToken = null,
    string? PreviousFirstMatchKey = null,
    Dictionary<string, long>? CarriedCounts = null);
