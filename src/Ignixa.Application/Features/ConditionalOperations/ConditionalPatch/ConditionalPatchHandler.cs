// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Patch;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Infrastructure;
using Ignixa.Search.Infrastructure;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.ConditionalOperations.ConditionalPatch;

/// <summary>
/// Handles conditional patch operations via query string.
/// FHIR R4 Section 3.1.0.7: Conditional Patch
///
/// Behavior:
/// - 0 matches: Return 404 Not Found (different from conditional update!)
/// - 1 match: Patch existing resource, return 200 OK
/// - Multiple matches: Return 412 Precondition Failed
/// </summary>
public class ConditionalPatchHandler : IRequestHandler<ConditionalPatchCommand, ConditionalPatchResult>
{
    private readonly IMediator _mediator;
    private readonly IQueryParameterParser _queryParser;
    private readonly ISearchOptionsBuilderFactory _searchOptionsBuilderFactory;
    private readonly IFhirRequestContextAccessor _contextAccessor;
    private readonly ILogger<ConditionalPatchHandler> _logger;

    public ConditionalPatchHandler(
        IMediator mediator,
        IQueryParameterParser queryParser,
        ISearchOptionsBuilderFactory searchOptionsBuilderFactory,
        IFhirRequestContextAccessor contextAccessor,
        ILogger<ConditionalPatchHandler> logger)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _queryParser = queryParser ?? throw new ArgumentNullException(nameof(queryParser));
        _searchOptionsBuilderFactory = searchOptionsBuilderFactory ?? throw new ArgumentNullException(nameof(searchOptionsBuilderFactory));
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ConditionalPatchResult> HandleAsync(
        ConditionalPatchCommand request,
        CancellationToken cancellationToken)
    {
        // Get FHIR request context (populated by FhirRequestContextMiddleware)
        var context = _contextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available");

        _logger.LogInformation(
            "Conditional patch: TenantId={TenantId}, ResourceType={ResourceType}, Criteria={SearchCriteria}",
            request.TenantId,
            request.ResourceType,
            request.SearchCriteria);

        // Step 1: Parse search criteria query string
        var queryParameters = _queryParser.Parse(request.SearchCriteria);

        // Step 2: Get FHIR version from context
        var fhirVersion = context.FhirVersion;
        var searchOptionsBuilder = _searchOptionsBuilderFactory.Create(fhirVersion);

        // Step 3: Build search options with _count=2 (we only need to know if 0, 1, or multiple)
        var searchOptions = searchOptionsBuilder.Build(request.ResourceType, queryParameters);
        searchOptions.MaxItemCount = 2;

        // Step 4: Execute search via SearchResourcesHandler
        var searchQuery = new SearchResourcesQuery(request.ResourceType, searchOptions);
        var searchResult = await _mediator.SendAsync(searchQuery, cancellationToken);

        // Step 5: Enumerate results to determine match count (materialize up to 2 results)
        var matches = new List<SearchEntryResult>();
        await foreach (var entry in searchResult.Resources.WithCancellation(cancellationToken))
        {
            matches.Add(entry);
            if (matches.Count >= 2)
            {
                break;  // Stop after finding multiple
            }
        }

        int matchCount = matches.Count;

        _logger.LogInformation(
            "Conditional patch search completed: Matches={MatchCount}",
            matchCount);

        // Step 2: Handle based on match count
        if (matchCount == 0)
        {
            // 0 matches: 404 Not Found (different from conditional update!)
            _logger.LogWarning("No matches found for conditional patch");
            throw new ConditionalOperationException(
                "ConditionalPatch",
                $"No resources match the search criteria '{request.SearchCriteria}'.",
                matchCount: 0,
                searchCriteria: request.SearchCriteria);
        }
        else if (matchCount == 1)
        {
            // 1 match: Patch existing resource
            var existingResource = matches[0];
            var existingId = existingResource.ResourceId;
            var existingVersionId = existingResource.VersionId;

            _logger.LogInformation(
                "One match found, patching existing resource: Id={ResourceId} (version {VersionId})",
                existingId,
                existingVersionId);

            // Pass IfMatch parameter for optimistic concurrency control
            // This prevents lost updates if someone modified the resource between search and patch
            var patchCommand = new PatchResourceCommand(
                request.TenantId,
                request.ResourceType,
                existingId,
                request.PatchDocument,
                IfMatch: existingVersionId); // Pass version ID for optimistic concurrency control

            var patchResult = await _mediator.SendAsync(patchCommand, cancellationToken);

            if (patchResult == null)
            {
                throw new InvalidOperationException(
                    $"Patch operation returned null for resource {request.ResourceType}/{existingId}");
            }

            return new ConditionalPatchResult(
                Resource: patchResult,
                MatchCount: 1);
        }
        else
        {
            // Multiple matches: Error (412 Precondition Failed)
            _logger.LogWarning(
                "Multiple matches found for conditional patch: Matches={MatchCount}",
                matchCount);

            throw new ConditionalOperationException(
                "ConditionalPatch",
                $"Multiple resources match the search criteria '{request.SearchCriteria}'. " +
                $"Found {matchCount} matches. Conditional patch requires exactly 1 match.",
                matchCount: matchCount,
                searchCriteria: request.SearchCriteria);
        }
    }
}
