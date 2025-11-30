// <copyright file="HistoryResult.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Domain.Models;
using Ignixa.Serialization.Models;

namespace Ignixa.Application.Features.History;

/// <summary>
/// Result from a history query operation, designed for streaming serialization.
/// Contains async enumerable entries and metadata for pagination.
/// </summary>
public sealed record HistoryResult
{
    /// <summary>
    /// Stream of history entries (truly async - not materialized into a list).
    /// Entries are yielded incrementally for optimal memory usage.
    /// </summary>
    public required IAsyncEnumerable<SearchEntryResult> Entries { get; init; }

    /// <summary>
    /// Total number of versions across all pages.
    /// Null unless client explicitly requests _total=accurate (requires separate count query).
    /// Default: null (most performant, no total calculation).
    /// </summary>
    public int? TotalCount { get; init; }

    /// <summary>
    /// Pagination links (self, first, prev, next, last).
    /// </summary>
    public required IReadOnlyList<BundleLinkJsonNode> Links { get; init; }
}
