// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.Extensions.Logging;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;

namespace Ignixa.Application.Features.Resource;

/// <summary>
/// Generic handler for searching any FHIR resource type.
/// Multi-tenant enabled: Uses IPartitionStrategy + IQueryExecutionStrategy.
///
/// Flow:
/// 1. Get tenant context from IFhirRequestContextAccessor
/// 2. Determine partition(s) using IPartitionStrategy
/// 3. Execute using IQueryExecutionStrategy
///    - PassthroughExecutionStrategy: Validates single partition, direct search service call
///    - FanoutExecutionStrategy (future): Handles multiple partitions with fanout/merge
/// </summary>
public class SearchResourcesHandler : IRequestHandler<SearchResourcesQuery, SearchResourcesResult>
{
    private readonly IPartitionStrategy _partitionStrategy;
    private readonly IQueryExecutionStrategy _executionStrategy;
    private readonly IFhirRequestContextAccessor _contextAccessor;
    private readonly ILogger<SearchResourcesHandler> _logger;

    public SearchResourcesHandler(
        IPartitionStrategy partitionStrategy,
        IQueryExecutionStrategy executionStrategy,
        IFhirRequestContextAccessor contextAccessor,
        ILogger<SearchResourcesHandler> logger)
    {
        _partitionStrategy = partitionStrategy ?? throw new ArgumentNullException(nameof(partitionStrategy));
        _executionStrategy = executionStrategy ?? throw new ArgumentNullException(nameof(executionStrategy));
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SearchResourcesResult> HandleAsync(
        SearchResourcesQuery request,
        CancellationToken cancellationToken)
    {
        // Get FHIR request context (populated by FhirRequestContextMiddleware)
        var context = _contextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available");

        _logger.LogInformation("Searching for {ResourceType} resources (streaming)", request.ResourceType ?? "all resource types");

        // Create partition resolution context from FHIR request context
        var partitionContext = new PartitionResolutionContext
        {
            TenantId = context.TenantId,
            TenantConfiguration = context.TenantConfiguration
        };

        // 1. Determine partition(s) using IPartitionStrategy
        // Convert request.SearchOptions query parameters to Dictionary for partition strategy
        var queryParams = new Dictionary<string, string>(); // TODO: Extract from SearchOptions if needed

        var partition = _partitionStrategy.DetermineReadPartition(
            partitionContext,
            request.ResourceType,
            queryParams);

        _logger.LogDebug(
            "Partition(s) determined: [{PartitionIds}] (Mode: {Mode})",
            string.Join(",", partition.PartitionIds),
            partition.Mode);

        // 2. Request pageSize + 1 results to detect if there are more (count-as-render pattern)
        //    The serializer will render only pageSize items and use the +1 to detect hasMore
        var searchOptionsWithExtra = new SearchOptions
        {
            MaxItemCount = request.SearchOptions.MaxItemCount + 1,
            ContinuationToken = request.SearchOptions.ContinuationToken,
            Expression = request.SearchOptions.Expression,
            Sort = request.SearchOptions.Sort,
            Include = request.SearchOptions.Include,
            RevInclude = request.SearchOptions.RevInclude,
            Total = request.SearchOptions.Total,
            Summary = request.SearchOptions.Summary,
            UnsupportedParams = request.SearchOptions.UnsupportedParams,
            ResourceType = request.SearchOptions.ResourceType
        };

        _logger.LogDebug(
            "Requesting {RequestCount} results (pageSize={PageSize} + 1 for pagination detection)",
            searchOptionsWithExtra.MaxItemCount,
            request.SearchOptions.MaxItemCount);

        // 3. Execute query using IQueryExecutionStrategy with the +1 count
        //    - PassthroughExecutionStrategy: validates single partition, direct query
        //    - FanoutExecutionStrategy (future): can handle multiple partitions
        // This streams pageSize + 1 results (the serializer will count and detect hasMore)
        var resourceStream = _executionStrategy.SearchStreamAsync(
            partition,
            searchOptionsWithExtra,
            cancellationToken);

        // Calculate total count for Bundle.total field if explicitly requested
        int? total = null;
        if (request.SearchOptions.Total == TotalType.Accurate)
        {
            // Only execute COUNT query if explicitly requested
            int totalCount = await _executionStrategy.CountAsync(partition, request.SearchOptions, cancellationToken);
            _logger.LogDebug("Accurate total requested, COUNT query returned {TotalCount}", totalCount);
            total = totalCount;
        }
        else if (request.SearchOptions.Total == TotalType.Estimate)
        {
            // For estimate mode, we could implement a cheaper estimation strategy in the future
            // For now, execute count query
            int totalCount = await _executionStrategy.CountAsync(partition, request.SearchOptions, cancellationToken);
            _logger.LogDebug("Estimate total requested, using COUNT query result {TotalCount}", totalCount);
            total = totalCount;
        }
        // TotalType.None: total remains null (default, no COUNT query executed)

        var result = new SearchResourcesResult(
            Resources: resourceStream,
            Total: total,
            ContinuationToken: null, // Serializer will generate this based on count-as-render
            HasMore: false, // Serializer will determine this based on count-as-render
            SearchOptions: request.SearchOptions); // Original pageSize, not +1

        return result;
    }
}
