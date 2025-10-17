// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Ignixa.Application.Features;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Query execution strategy for Isolation mode (Phase 20).
/// Validates that exactly one partition is specified, then passes through directly to the search service.
///
/// Key Design:
/// - CRUD operations don't use this strategy (they use IFhirRepositoryFactory directly)
/// - Search operations use this strategy to validate single partition and stream results
/// - Zero overhead: just validation + direct search service call (no aggregation)
/// - Future: FanoutExecutionStrategy (Phase 20.2+) will handle multiple partitions
/// </summary>
public class PassthroughExecutionStrategy : IQueryExecutionStrategy
{
    private readonly ISearchServiceFactory _searchServiceFactory;
    private readonly ILogger<PassthroughExecutionStrategy> _logger;

    public PassthroughExecutionStrategy(
        ISearchServiceFactory searchServiceFactory,
        ILogger<PassthroughExecutionStrategy> logger)
    {
        _searchServiceFactory = searchServiceFactory ?? throw new ArgumentNullException(nameof(searchServiceFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async IAsyncEnumerable<SearchEntryResult> SearchStreamAsync<TSearchOptions>(
        RequestPartition partition,
        TSearchOptions searchOptions,
        [EnumeratorCancellation] CancellationToken ct = default)
        where TSearchOptions : class
    {
        // Validate single partition (Isolation mode requirement)
        if (partition.PartitionIds.Count != 1)
        {
            _logger.LogError(
                "Passthrough strategy requires exactly 1 partition ID, received {Count} partition IDs: [{PartitionIds}]",
                partition.PartitionIds.Count,
                string.Join(", ", partition.PartitionIds));
            throw new InvalidOperationException(
                $"Passthrough strategy requires exactly 1 partition ID. " +
                $"Received {partition.PartitionIds.Count} partition IDs. " +
                $"This indicates a configuration error or use of Distributed mode with Passthrough strategy.");
        }

        int tenantId = partition.PartitionIds[0];

        _logger.LogDebug(
            "Executing passthrough search for tenant {TenantId} (Mode: {Mode})",
            tenantId,
            partition.Mode);

        // Get search service for the single partition
        var searchService = await _searchServiceFactory.GetSearchServiceAsync(tenantId, ct);

        // Stream results directly (no aggregation needed)
        await foreach (var resource in searchService.SearchStreamAsync(searchOptions, ct))
        {
            yield return resource;
        }
    }

    public async ValueTask<int> CountAsync<TSearchOptions>(
        RequestPartition partition,
        TSearchOptions searchOptions,
        CancellationToken ct = default)
        where TSearchOptions : class
    {
        // Validate single partition (Isolation mode requirement)
        if (partition.PartitionIds.Count != 1)
        {
            _logger.LogError(
                "Passthrough strategy requires exactly 1 partition ID for count, received {Count} partition IDs: [{PartitionIds}]",
                partition.PartitionIds.Count,
                string.Join(", ", partition.PartitionIds));
            throw new InvalidOperationException(
                $"Passthrough strategy requires exactly 1 partition ID. " +
                $"Received {partition.PartitionIds.Count} partition IDs.");
        }

        int tenantId = partition.PartitionIds[0];

        _logger.LogDebug(
            "Executing passthrough count for tenant {TenantId} (Mode: {Mode})",
            tenantId,
            partition.Mode);

        // Get search service for the single partition
        var searchService = await _searchServiceFactory.GetSearchServiceAsync(tenantId, ct);

        // Count directly
        return await searchService.CountAsync(searchOptions, ct);
    }
}
