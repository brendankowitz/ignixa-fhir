// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;
using Ignixa.Search.Models;

namespace Ignixa.Application.Features.Resource;

/// <summary>
/// Result of a generic resource search query with streaming support.
/// Uses SearchEntryResult for zero-copy serialization (read path with raw bytes).
/// </summary>
/// <param name="Resources">The async stream of matching resources (as raw bytes).</param>
/// <param name="Total">The total count of matching resources (if requested).</param>
/// <param name="ContinuationToken">Token for fetching the next page of results.</param>
/// <param name="HasMore">Indicates if there are more results beyond the current page (used for next link generation).</param>
/// <param name="SearchOptions">The search options used for this query (for link generation with sort, filters, etc.).</param>
public record SearchResourcesResult(
    IAsyncEnumerable<SearchEntryResult> Resources,
    int? Total = null,
    string? ContinuationToken = null,
    bool HasMore = false,
    SearchOptions? SearchOptions = null);
