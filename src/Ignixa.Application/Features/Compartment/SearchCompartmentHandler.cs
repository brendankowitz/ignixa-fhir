// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.Extensions.Logging;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;

namespace Ignixa.Application.Features.Compartment;

/// <summary>
/// Handler for compartment search queries.
/// Creates a CompartmentSearchExpression which is passed to the search execution strategy.
/// The data layer (SearchExpressionQueryBuilder) detects CompartmentSearchExpression
/// and delegates to CompartmentSearchQueryGenerator for optimized query generation.
///
/// Flow:
/// 1. Create CompartmentSearchExpression for the requested compartment
/// 2. Add the expression to SearchOptions
/// 3. Delegate to normal search execution strategy
/// 4. Data layer intercepts CompartmentSearchExpression and optimizes with CompartmentSearchQueryGenerator
/// </summary>
public class SearchCompartmentHandler : IRequestHandler<SearchCompartmentQuery, SearchResourcesResult>
{
    private readonly IPartitionStrategy _partitionStrategy;
    private readonly IQueryExecutionStrategy _executionStrategy;
    private readonly IFhirRequestContextAccessor _contextAccessor;
    private readonly ILogger<SearchCompartmentHandler> _logger;

    public SearchCompartmentHandler(
        IPartitionStrategy partitionStrategy,
        IQueryExecutionStrategy executionStrategy,
        IFhirRequestContextAccessor contextAccessor,
        ILogger<SearchCompartmentHandler> logger)
    {
        _partitionStrategy = partitionStrategy ?? throw new ArgumentNullException(nameof(partitionStrategy));
        _executionStrategy = executionStrategy ?? throw new ArgumentNullException(nameof(executionStrategy));
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<SearchResourcesResult> HandleAsync(
        SearchCompartmentQuery request,
        CancellationToken cancellationToken)
    {
        // Get FHIR request context (populated by FhirRequestContextMiddleware)
        var context = _contextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available");

        _logger.LogInformation(
            "Searching compartment {CompartmentType}/{CompartmentId} for {ResourceType} resources",
            request.CompartmentType,
            request.CompartmentId,
            request.ResourceType);

        // Create CompartmentSearchExpression with optional resource type filtering
        // If ResourceType is "*", we search all types in the compartment (empty FilteredResourceTypes)
        // If ResourceType is specific (e.g., "Observation"), we only search that type
        ISet<string>? filteredResourceTypes = null;
        if (request.ResourceType != "*" && !string.IsNullOrEmpty(request.ResourceType))
        {
            filteredResourceTypes = new HashSet<string> { request.ResourceType };
        }

        var compartmentExpression = new CompartmentSearchExpression(
            request.CompartmentType,
            request.CompartmentId,
            filteredResourceTypes);

        _logger.LogDebug(
            "Created compartment search expression: {Expression} with filtered resource types: [{FilteredTypes}]",
            compartmentExpression,
            filteredResourceTypes == null ? "all" : string.Join(",", filteredResourceTypes));

        // Combine with existing search options
        // If SearchOptions.Expression already exists, AND it with the compartment expression
        Expression finalExpression = request.SearchOptions.Expression != null
            ? Expression.And(compartmentExpression, request.SearchOptions.Expression)
            : compartmentExpression;

        // Update SearchOptions with the combined expression (SearchOptions is mutable class)
        request.SearchOptions.Expression = finalExpression;

        // 4. Determine partition(s) using IPartitionStrategy
        var partitionContext = new PartitionResolutionContext
        {
            TenantId = context.TenantId,
            TenantConfiguration = context.TenantConfiguration
        };

        var queryParams = new Dictionary<string, string>(); // TODO: Extract from SearchOptions if needed

        // For wildcard searches, use a generic resource type for partition determination
        var resourceTypeForPartition = request.ResourceType == "*" ? "Resource" : request.ResourceType;

        var partition = _partitionStrategy.DetermineReadPartition(
            partitionContext,
            resourceTypeForPartition,
            queryParams);

        _logger.LogDebug(
            "Partition(s) determined: [{PartitionIds}] (Mode: {Mode})",
            string.Join(",", partition.PartitionIds),
            partition.Mode);

        // 5. For wildcard compartment searches, clear ResourceType from SearchOptions
        // so the search service doesn't try to validate "*" as a resource type.
        // The compartment search expression will handle resource type filtering.
        if (request.ResourceType == "*")
        {
            request.SearchOptions.ResourceType = null;

            _logger.LogDebug(
                "Wildcard compartment search detected - cleared ResourceType from SearchOptions");
        }

        // 6. Execute using IQueryExecutionStrategy (same as regular search)
        // The compartment search expression will be intercepted by SearchExpressionQueryBuilder
        // which delegates to CompartmentSearchQueryGenerator for optimized query generation
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
