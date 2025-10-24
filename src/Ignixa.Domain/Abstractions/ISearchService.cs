// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Service for searching FHIR resources.
/// This abstraction is separate from IFhirRepository to avoid circular dependencies with SearchOptions.
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Streams search results asynchronously for memory-efficient processing.
    /// Resources are yielded as they are retrieved, enabling progressive serialization.
    /// Returns raw JSON bytes for zero-copy serialization to HTTP response.
    /// </summary>
    /// <typeparam name="TSearchOptions">The type of search options (e.g., SearchOptions from Sparky.Search).</typeparam>
    /// <param name="searchOptions">The search criteria.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async stream of matching resources with raw bytes.</returns>
    IAsyncEnumerable<SearchEntryResult> SearchStreamAsync<TSearchOptions>(
        TSearchOptions searchOptions,
        CancellationToken ct = default)
        where TSearchOptions : class;

    /// <summary>
    /// Counts the number of resources that match the search criteria.
    /// This is an optimized query that does not retrieve resource data, sort results, or process _include/_revinclude.
    /// </summary>
    /// <typeparam name="TSearchOptions">The type of search options (e.g., SearchOptions from Sparky.Search).</typeparam>
    /// <param name="searchOptions">The search criteria (only filter parameters are used; _sort, _include, _revinclude are ignored).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The count of matching resources.</returns>
    ValueTask<int> CountAsync<TSearchOptions>(
        TSearchOptions searchOptions,
        CancellationToken ct = default)
        where TSearchOptions : class;
}
