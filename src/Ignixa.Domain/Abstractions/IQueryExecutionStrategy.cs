// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Executes FHIR search queries against determined partition(s).
///
/// KEY DISTINCTION FROM IFhirRepositoryFactory:
/// - CRUD operations (Get/Create/Update/Delete) always target a SINGLE partition → use IFhirRepositoryFactory directly
/// - Search operations may need to query MULTIPLE partitions (Distributed mode fanout) → use IQueryExecutionStrategy
///
/// This separation keeps CRUD simple (direct repository access) while isolating search complexity (fanout/merge logic).
///
/// Implementations:
/// - PassthroughExecutionStrategy (Phase 20 - Isolation Mode): Validates single partition, direct search service call
/// - FanoutExecutionStrategy (Phase 20.2+ - Distributed Mode): Fans out to multiple shards, merges results
/// </summary>
public interface IQueryExecutionStrategy
{
    /// <summary>
    /// Streams search results from the determined partition(s).
    ///
    /// Isolation Mode (PassthroughExecutionStrategy):
    /// - Validates partition.PartitionIds.Length == 1
    /// - Gets search service from ISearchServiceFactory for the single partition
    /// - Streams results directly (no aggregation)
    ///
    /// Distributed Mode (FanoutExecutionStrategy - Phase 20.2+):
    /// - If partition.PartitionIds.Length == 1, bypasses fanout (optimization)
    /// - If partition.PartitionIds.Length > 1, fans out to multiple shards in parallel
    /// - Merges streams with proper sorting and deduplication
    /// </summary>
    /// <typeparam name="TSearchOptions">Search options type (SearchOptions, InMemorySearchOptions, etc.)</typeparam>
    /// <param name="partition">The partition(s) determined by IPartitionStrategy</param>
    /// <param name="searchOptions">Parsed search parameters and options</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Stream of matching SearchEntryResult instances (raw bytes for zero-copy serialization)</returns>
    IAsyncEnumerable<SearchEntryResult> SearchStreamAsync<TSearchOptions>(
        RequestPartition partition,
        TSearchOptions searchOptions,
        CancellationToken ct = default)
        where TSearchOptions : class;

    /// <summary>
    /// Counts matching resources in the determined partition(s).
    ///
    /// Isolation Mode (PassthroughExecutionStrategy):
    /// - Validates single partition
    /// - Returns count from single search service
    ///
    /// Distributed Mode (FanoutExecutionStrategy - Phase 20.2+):
    /// - Fans out count to all shards in parallel
    /// - Sums results
    /// </summary>
    /// <typeparam name="TSearchOptions">Search options type</typeparam>
    /// <param name="partition">The partition(s) determined by IPartitionStrategy</param>
    /// <param name="searchOptions">Parsed search parameters and options</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Total count across all partitions</returns>
    ValueTask<int> CountAsync<TSearchOptions>(
        RequestPartition partition,
        TSearchOptions searchOptions,
        CancellationToken ct = default)
        where TSearchOptions : class;
}
