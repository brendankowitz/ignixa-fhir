// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Ignixa.Application.Features.Resource;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;

namespace Ignixa.Application.Features.Compartment;

/// <summary>
/// Handler for compartment search queries.
/// Uses CompartmentSearchRewriter to expand compartment constraints into search parameter expressions,
/// then executes via the normal search pipeline.
///
/// Flow:
/// 1. Create CompartmentSearchExpression for the requested compartment
/// 2. Use CompartmentSearchRewriter to expand into OR'd search parameter expressions
/// 3. Add the rewritten expression to SearchOptions
/// 4. Delegate to normal search execution strategy
/// </summary>
public class SearchCompartmentHandler : IRequestHandler<SearchCompartmentQuery, SearchResourcesResult>
{
    private readonly IPartitionStrategy _partitionStrategy;
    private readonly IQueryExecutionStrategy _executionStrategy;
    private readonly ICompartmentDefinitionManager _compartmentDefinitionManager;
    private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<SearchCompartmentHandler> _logger;

    public SearchCompartmentHandler(
        IPartitionStrategy partitionStrategy,
        IQueryExecutionStrategy executionStrategy,
        ICompartmentDefinitionManager compartmentDefinitionManager,
        ISearchParameterDefinitionManager searchParameterDefinitionManager,
        IHttpContextAccessor httpContextAccessor,
        ILogger<SearchCompartmentHandler> logger)
    {
        _partitionStrategy = partitionStrategy ?? throw new ArgumentNullException(nameof(partitionStrategy));
        _executionStrategy = executionStrategy ?? throw new ArgumentNullException(nameof(executionStrategy));
        _compartmentDefinitionManager = compartmentDefinitionManager ?? throw new ArgumentNullException(nameof(compartmentDefinitionManager));
        _searchParameterDefinitionManager = searchParameterDefinitionManager ?? throw new ArgumentNullException(nameof(searchParameterDefinitionManager));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<SearchResourcesResult> HandleAsync(
        SearchCompartmentQuery request,
        CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HttpContext is null");

        _logger.LogInformation(
            "Searching compartment {CompartmentType}/{CompartmentId} for {ResourceType} resources",
            request.CompartmentType,
            request.CompartmentId,
            request.ResourceType);

        // Extract tenant context from HttpContext.Items
        if (!httpContext.Items.TryGetValue("TenantId", out var tenantIdObj) || tenantIdObj is not int tenantId)
        {
            throw new InvalidOperationException("TenantId not found in HttpContext.Items");
        }

        var tenantConfig = httpContext.Items["TenantConfiguration"] as TenantConfiguration;

        // 1. Create CompartmentSearchExpression
        var compartmentExpression = new CompartmentSearchExpression(
            request.CompartmentType,
            request.CompartmentId);

        // 2. Use CompartmentSearchRewriter to expand into search parameter expressions
        var rewriter = new CompartmentSearchRewriter();
        var rewriterContext = (
            ResourceType: request.ResourceType,
            CompartmentManager: _compartmentDefinitionManager,
            SearchParameterManager: _searchParameterDefinitionManager);

        var rewrittenExpression = rewriter.VisitCompartment(compartmentExpression, rewriterContext);

        _logger.LogDebug(
            "Compartment expression rewritten: {Original} -> {Rewritten}",
            compartmentExpression,
            rewrittenExpression);

        // 3. Combine rewritten expression with existing search options
        // If SearchOptions.Expression already exists, AND it with the compartment expression
        var finalExpression = request.SearchOptions.Expression != null
            ? Expression.And(rewrittenExpression, request.SearchOptions.Expression)
            : rewrittenExpression;

        // Update SearchOptions with the combined expression (SearchOptions is mutable class)
        request.SearchOptions.Expression = finalExpression;

        // 4. Determine partition(s) using IPartitionStrategy
        var partitionContext = new PartitionResolutionContext
        {
            TenantId = tenantId,
            TenantConfiguration = tenantConfig
        };

        var queryParams = new Dictionary<string, string>(); // TODO: Extract from SearchOptions if needed

        var partition = _partitionStrategy.DetermineReadPartition(
            partitionContext,
            request.ResourceType,
            queryParams);

        _logger.LogDebug(
            "Partition(s) determined: [{PartitionIds}] (Mode: {Mode})",
            string.Join(",", partition.PartitionIds),
            partition.Mode);

        // 5. Execute using IQueryExecutionStrategy (same as regular search)
        var resourceStream = _executionStrategy.SearchStreamAsync(
            partition,
            request.SearchOptions,
            cancellationToken);

        // TODO: Calculate total count if requested (Phase 1.2a)
        int? total = null;
        if (request.SearchOptions.Total != TotalType.None)
        {
            _logger.LogWarning(
                "Total count requested but not yet supported with streaming for compartment search on {ResourceType}",
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
