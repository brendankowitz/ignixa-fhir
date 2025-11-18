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

namespace Ignixa.Application.Operations.Features.PatientEverything;

/// <summary>
/// Handler for Patient $everything operation queries.
/// Creates a PatientEverythingExpression which is passed to the search execution strategy.
/// The data layer (SearchExpressionQueryBuilder) detects PatientEverythingExpression
/// and delegates to PatientEverythingQueryGenerator for optimized single-query generation.
///
/// Flow:
/// 1. Create PatientEverythingExpression for the requested patient
/// 2. Add the expression to SearchOptions
/// 3. Delegate to normal search execution strategy
/// 4. Data layer intercepts PatientEverythingExpression and optimizes with PatientEverythingQueryGenerator
/// </summary>
public class PatientEverythingHandler : IRequestHandler<PatientEverythingQuery, SearchResourcesResult>
{
    private readonly IPartitionStrategy _partitionStrategy;
    private readonly IQueryExecutionStrategy _executionStrategy;
    private readonly IFhirRequestContextAccessor _contextAccessor;
    private readonly ILogger<PatientEverythingHandler> _logger;

    public PatientEverythingHandler(
        IPartitionStrategy partitionStrategy,
        IQueryExecutionStrategy executionStrategy,
        IFhirRequestContextAccessor contextAccessor,
        ILogger<PatientEverythingHandler> logger)
    {
        _partitionStrategy = partitionStrategy ?? throw new ArgumentNullException(nameof(partitionStrategy));
        _executionStrategy = executionStrategy ?? throw new ArgumentNullException(nameof(executionStrategy));
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<SearchResourcesResult> HandleAsync(
        PatientEverythingQuery request,
        CancellationToken cancellationToken)
    {
        // Get FHIR request context (populated by FhirRequestContextMiddleware)
        var context = _contextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available");

        _logger.LogInformation(
            "Executing Patient $everything for patient {PatientId}",
            request.PatientId);

        // Create PatientEverythingExpression with all filters
        var patientEverythingExpression = new PatientEverythingExpression(
            patientId: request.PatientId,
            startDate: request.Start,
            endDate: request.End,
            sinceDate: request.Since,
            filteredResourceTypes: request.Types,
            includeReferencedResources: true); // Always include referenced resources per FHIR spec

        _logger.LogDebug(
            "Created Patient $everything expression: {Expression}",
            patientEverythingExpression);

        // Create SearchOptions with the PatientEverythingExpression
        // Note: ResourceType is null because $everything returns multiple resource types
        var searchOptions = new SearchOptions
        {
            ResourceType = null, // Multi-resource type search
            Expression = patientEverythingExpression,
            Count = request.Count ?? 50, // Default to 50 if not specified
            SortParams = null, // TODO: Support _sort parameter in future
            IncludeParams = null, // Not applicable for $everything (already includes everything)
            RevIncludeParams = null, // Not applicable for $everything
            Total = TotalType.None, // TODO: Support total count in future
            SummaryType = null,
            ElementsParams = null
        };

        // Determine partition(s) using IPartitionStrategy
        var partitionContext = new PartitionResolutionContext
        {
            TenantId = context.TenantId,
            TenantConfiguration = context.TenantConfiguration
        };

        var queryParams = new Dictionary<string, string>();

        var partition = _partitionStrategy.DetermineReadPartition(
            partitionContext,
            "Patient", // Use Patient as the primary resource type
            queryParams);

        _logger.LogDebug(
            "Partition(s) determined: [{PartitionIds}] (Mode: {Mode})",
            string.Join(",", partition.PartitionIds),
            partition.Mode);

        // Execute using IQueryExecutionStrategy (same as regular search)
        // The PatientEverythingExpression will be intercepted by SearchExpressionQueryBuilder
        // which delegates to PatientEverythingQueryGenerator for optimized single-query generation
        var resourceStream = _executionStrategy.SearchStreamAsync(
            partition,
            searchOptions,
            cancellationToken);

        // TODO: Calculate total count if requested
        int? total = null;
        if (searchOptions.Total != TotalType.None)
        {
            _logger.LogWarning(
                "Total count requested but not yet supported for Patient $everything on patient {PatientId}",
                request.PatientId);
            total = null;
        }

        var result = new SearchResourcesResult(
            Resources: resourceStream,
            Total: total,
            ContinuationToken: null); // TODO: Implement paging token generation

        return Task.FromResult(result);
    }
}
