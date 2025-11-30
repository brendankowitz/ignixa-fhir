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
public class PatientEverythingHandler(
    IPartitionStrategy partitionStrategy,
    IQueryExecutionStrategy executionStrategy,
    IFhirRequestContextAccessor contextAccessor,
    ILogger<PatientEverythingHandler> logger) : IRequestHandler<PatientEverythingQuery, SearchResourcesResult>
{

    public Task<SearchResourcesResult> HandleAsync(
        PatientEverythingQuery request,
        CancellationToken cancellationToken)
    {
        // Get FHIR request context (populated by FhirRequestContextMiddleware)
        var context = contextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available");

        logger.LogInformation(
            "Executing Patient $everything for patient {PatientId}",
            request.PatientId);

        // Create PatientEverythingExpression with all filters
        // Only include referenced resources (Practitioner, Organization, etc.) when NO _type filter is specified
        // When _type is specified, only return resources of those exact types
        var includeReferencedResources = request.Types == null || request.Types.Count == 0;

        var patientEverythingExpression = new PatientEverythingExpression(
            patientId: request.PatientId,
            startDate: request.Start,
            endDate: request.End,
            sinceDate: request.Since,
            filteredResourceTypes: request.Types,
            includeReferencedResources: includeReferencedResources);

        logger.LogDebug(
            "Created Patient $everything expression: {Expression}",
            patientEverythingExpression);

        // Create SearchOptions with the PatientEverythingExpression
        // Note: ResourceType is null because $everything returns multiple resource types
        var searchOptions = new SearchOptions
        {
            ResourceType = null, // Multi-resource type search
            Expression = patientEverythingExpression,
            MaxItemCount = request.Count ?? 50, // Default to 50 if not specified
            Sort = [], // _sort parameter not currently supported for $everything
            Include = [], // Not applicable for $everything (already includes everything)
            RevInclude = [], // Not applicable for $everything
            Total = TotalType.None, // Total count calculation not currently enabled for $everything
            Summary = SummaryType.False,
            Elements = new HashSet<string>()
        };

        // Determine partition(s) using IPartitionStrategy
        var partitionContext = new PartitionResolutionContext
        {
            TenantId = context.TenantId,
            TenantConfiguration = context.TenantConfiguration
        };

        var queryParams = new Dictionary<string, string>();

        var partition = partitionStrategy.DetermineReadPartition(
            partitionContext,
            "Patient", // Use Patient as the primary resource type
            queryParams);

        logger.LogDebug(
            "Partition(s) determined: [{PartitionIds}] (Mode: {Mode})",
            string.Join(",", partition.PartitionIds),
            partition.Mode);

        // Execute using IQueryExecutionStrategy (same as regular search)
        // The PatientEverythingExpression will be intercepted by SearchExpressionQueryBuilder
        // which delegates to PatientEverythingQueryGenerator for optimized single-query generation
        var resourceStream = executionStrategy.SearchStreamAsync(
            partition,
            searchOptions,
            cancellationToken);

        // Total count calculation not currently implemented for $everything operation
        int? total = null;
        if (searchOptions.Total != TotalType.None)
        {
            logger.LogWarning(
                "Total count requested but not currently supported for Patient $everything on patient {PatientId}",
                request.PatientId);
            total = null;
        }

        var result = new SearchResourcesResult(
            Resources: resourceStream,
            Total: total,
            ContinuationToken: null, // Paging tokens not yet generated for $everything
            HasMore: false, // HasMore detection not yet implemented for $everything
            SearchOptions: searchOptions); // Include SearchOptions for bundle serialization

        return Task.FromResult(result);
    }
}
