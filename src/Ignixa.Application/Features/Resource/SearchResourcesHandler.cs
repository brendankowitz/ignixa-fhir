// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;

namespace Ignixa.Application.Features.Resource;

/// <summary>
/// Generic handler for searching any FHIR resource type.
/// Multi-tenant enabled: Uses IPartitionStrategy + IQueryExecutionStrategy.
///
/// Flow:
/// 1. Determine partition(s) using IPartitionStrategy (reads tenantId from HttpContext.Items)
/// 2. Execute using IQueryExecutionStrategy
///    - PassthroughExecutionStrategy: Validates single partition, direct search service call
///    - FanoutExecutionStrategy (future): Handles multiple partitions with fanout/merge
/// </summary>
public class SearchResourcesHandler : IRequestHandler<SearchResourcesQuery, SearchResourcesResult>
{
    private readonly IPartitionStrategy _partitionStrategy;
    private readonly IQueryExecutionStrategy _executionStrategy;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<SearchResourcesHandler> _logger;

    public SearchResourcesHandler(
        IPartitionStrategy partitionStrategy,
        IQueryExecutionStrategy executionStrategy,
        IHttpContextAccessor httpContextAccessor,
        ILogger<SearchResourcesHandler> logger)
    {
        _partitionStrategy = partitionStrategy ?? throw new ArgumentNullException(nameof(partitionStrategy));
        _executionStrategy = executionStrategy ?? throw new ArgumentNullException(nameof(executionStrategy));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<SearchResourcesResult> HandleAsync(
        SearchResourcesQuery request,
        CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HttpContext is null");

        _logger.LogInformation("Searching for {ResourceType} resources (streaming)", request.ResourceType);

        // Extract tenant context from HttpContext.Items
        if (!httpContext.Items.TryGetValue("TenantId", out var tenantIdObj) || tenantIdObj is not int tenantId)
        {
            throw new InvalidOperationException("TenantId not found in HttpContext.Items");
        }

        var tenantConfig = httpContext.Items["TenantConfiguration"] as TenantConfiguration;

        // Create partition resolution context
        var partitionContext = new PartitionResolutionContext
        {
            TenantId = tenantId,
            TenantConfiguration = tenantConfig
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

        // 2. Execute using IQueryExecutionStrategy
        //    - PassthroughExecutionStrategy: validates single partition, direct query
        //    - FanoutExecutionStrategy (future): can handle multiple partitions
        var resourceStream = _executionStrategy.SearchStreamAsync(
            partition,
            request.SearchOptions,
            cancellationToken);

        // TODO: Calculate total count if requested (Phase 1.2a)
        // Note: Calculating total with streaming requires either:
        // 1. Separate count query (recommended)
        // 2. Buffering all results (defeats streaming purpose)
        // 3. Return null total (current approach)
        int? total = null;
        if (request.SearchOptions.Total != TotalType.None)
        {
            // For now, return null - will implement separate count query in Phase 1.2a
            _logger.LogWarning("Total count requested but not yet supported with streaming for {ResourceType}",
                request.ResourceType);
            total = null;
        }

        var result = new SearchResourcesResult(
            Resources: resourceStream,
            Total: total,
            ContinuationToken: null); // TODO: Implement paging in Phase 1.2a

        return Task.FromResult(result);
    }
}
