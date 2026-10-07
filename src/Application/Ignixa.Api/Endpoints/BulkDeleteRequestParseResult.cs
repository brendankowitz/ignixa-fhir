// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;

namespace Ignixa.Api.Endpoints;

/// <summary>
/// The outcome of <see cref="BulkDeleteRequestParser.Parse"/>: the effective deletion mode and flags,
/// plus the query parameters that remain once the bulk-delete control parameters are removed. The
/// remaining parameters (including <c>_type</c>) feed <see cref="Ignixa.Application.BackgroundOperations.BulkDelete.CreateBulkDeleteJobCommand.SearchParameters"/>
/// unchanged.
/// </summary>
/// <param name="Mode">Effective deletion mode (hard takes precedence over purge, per the design).</param>
/// <param name="RemoveReferences">Whether referrers of each deleted resource should be rewritten.</param>
/// <param name="ExcludedResourceTypes">Resource types named by <c>excludedResourceTypes</c>, flattened and trimmed.</param>
/// <param name="SearchParameters">Query parameters with the bulk-delete control parameters removed, original order preserved.</param>
public sealed record BulkDeleteRequestParseResult(
    BulkDeleteMode Mode,
    bool RemoveReferences,
    IReadOnlyList<string> ExcludedResourceTypes,
    IReadOnlyList<KeyValuePair<string, string>> SearchParameters);
