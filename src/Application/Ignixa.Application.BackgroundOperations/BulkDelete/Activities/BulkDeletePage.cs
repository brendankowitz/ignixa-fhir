// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.BulkDelete.Activities;

/// <summary>
/// One search page of a bulk-delete batch.
/// </summary>
/// <param name="Targets">Resources to delete, includes first, de-duplicated by type and id.</param>
/// <param name="MatchCount">How many targets are matches (the tail of <paramref name="Targets"/>).</param>
/// <param name="HasMore">The search found more matches than the batch size.</param>
/// <param name="NextContinuationToken">Purge-history cursor for the next page; null in restart mode.</param>
/// <param name="FirstMatchKey"><c>Type/id</c> of the first match; null when the page had none.</param>
internal sealed record BulkDeletePage(
    IReadOnlyList<SearchEntryResult> Targets,
    int MatchCount,
    bool HasMore,
    string? NextContinuationToken,
    string? FirstMatchKey);
