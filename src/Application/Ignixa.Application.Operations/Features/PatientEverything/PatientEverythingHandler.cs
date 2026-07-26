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
/// <remarks>
/// Stated non-goals, recorded here because this handler is where each would be decided and because
/// silence about an absent behaviour is what let the last $everything defect survive unnoticed.
/// <para>
/// <b>Patient <c>link</c> is not followed.</b> The shipping engine resolves <c>link.type = seealso</c> one
/// layer deep, adding the linked patient's compartment to the result, and under
/// <c>Prefer: handling=strict</c> answers <c>link.type = replaced-by</c> with a 301 and an
/// OperationOutcome. Neither Ignixa path does any of it, and this is a non-goal rather than an oversight:
/// following <c>seealso</c> turns a single-patient operation into a graph walk whose termination depends
/// on data (the spec bounds it only by convention, not by cardinality), and the <c>replaced-by</c>
/// redirect is an HTTP-status concern that would have to be decided above the handler, in the endpoint,
/// where the <c>Prefer</c> header and the response status actually live. Both are additive: the
/// expression already accepts multiple patient ids (Group $everything uses it), so a future
/// <c>seealso</c> resolution is a pre-handler id expansion, not a change to the query.
/// </para>
/// <para>
/// <b>R5's Provenance/AuditEvent suggestion is not implemented.</b> R5 adds that servers "should consider
/// returning appropriate Provenance and AuditTrail" for $everything. Not done, and deliberately so: it is
/// a SHOULD introduced in a version this operation is not yet specialised for, and both types are
/// target-referencing (Provenance.target, AuditEvent.entity point <em>at</em> the returned resources), so
/// satisfying it is a reverse traversal over the whole result set rather than another member type -- a
/// different query shape, not another entry in a list.
/// </para>
/// </remarks>
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

        // Referenced-resource expansion is suppressed outright whenever _type is present, because the
        // legacy EF generator's expansion applies no type filter of its own -- $everything?_type=Encounter
        // would otherwise return Practitioners the caller excluded. Coarser than it needs to be
        // (_type=Practitioner also loses the expansion, where a referenced Practitioner is exactly what was
        // asked for); the compiler narrows the same case precisely, in ResolveReferencedTypeIds, so
        // relaxing this guard is a legacy-engine change rather than a compiler one.
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
        // ResourceType names the anchor -- the compartment root whose expansion Lower dispatches to
        // PatientEverythingExpression, not a filter on what comes back. The many resource types
        // $everything returns come from the compartment traversal, not from a null anchor here.
        var searchOptions = new SearchOptions
        {
            ResourceType = "Patient",
            Expression = patientEverythingExpression,
            MaxItemCount = request.Count ?? 50, // Default to 50 if not specified
            Sort = [], // _sort parameter not currently supported for $everything
            Include = [], // Not applicable for $everything (already includes everything)
            RevInclude = [], // Not applicable for $everything
            Total = TotalType.None, // Total count calculation not currently enabled for $everything
            Summary = Ignixa.Search.Models.SummaryType.False,
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
