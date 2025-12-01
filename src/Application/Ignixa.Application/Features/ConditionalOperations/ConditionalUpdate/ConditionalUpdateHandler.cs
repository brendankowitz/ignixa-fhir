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
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Features.ConditionalOperations.ConditionalUpdate;

/// <summary>
/// Handles conditional update operations via query string.
/// FHIR R4 Section 3.1.0.6: Conditional Update
///
/// Behavior:
/// - 0 matches: Create new resource (generate ID), return 201 Created
/// - 1 match: Update existing resource, return 200 OK
/// - Multiple matches: Return 412 Precondition Failed
/// </summary>
public class ConditionalUpdateHandler : IRequestHandler<ConditionalUpdateCommand, ConditionalUpdateResult>
{
    private readonly IMediator _mediator;
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly IQueryParameterParser _queryParser;
    private readonly ISearchOptionsBuilderFactory _searchOptionsBuilderFactory;
    private readonly IFhirRequestContextAccessor _contextAccessor;
    private readonly ILogger<ConditionalUpdateHandler> _logger;

    public ConditionalUpdateHandler(
        IMediator mediator,
        IFhirRepositoryFactory repositoryFactory,
        IQueryParameterParser queryParser,
        ISearchOptionsBuilderFactory searchOptionsBuilderFactory,
        IFhirRequestContextAccessor contextAccessor,
        ILogger<ConditionalUpdateHandler> logger)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _queryParser = queryParser ?? throw new ArgumentNullException(nameof(queryParser));
        _searchOptionsBuilderFactory = searchOptionsBuilderFactory ?? throw new ArgumentNullException(nameof(searchOptionsBuilderFactory));
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ConditionalUpdateResult> HandleAsync(
        ConditionalUpdateCommand request,
        CancellationToken cancellationToken)
    {
        // Get FHIR request context (populated by FhirRequestContextMiddleware)
        var context = _contextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available");

        _logger.LogInformation(
            "Processing conditional update for {ResourceType} with search criteria: {SearchCriteria}",
            request.ResourceType,
            request.SearchCriteria);

        // 1. Parse search criteria query string
        var queryParameters = _queryParser.Parse(request.SearchCriteria);

        // 2. Get FHIR version from context
        var fhirVersion = context.FhirVersion;
        var searchOptionsBuilder = _searchOptionsBuilderFactory.Create(fhirVersion);

        // 3. Build search options with _count=2 (we only need to know if 0, 1, or multiple)
        var searchOptions = searchOptionsBuilder.Build(request.ResourceType, queryParameters);
        searchOptions.MaxItemCount = 2;

        // 4. Execute search via SearchResourcesHandler
        var searchQuery = new SearchResourcesQuery(request.ResourceType, searchOptions);
        var searchResult = await _mediator.SendAsync(searchQuery, cancellationToken);

        // 5. Enumerate results to determine match count (materialize up to 2 results)
        var matches = new List<SearchEntryResult>();
        await foreach (var match in searchResult.Resources.WithCancellation(cancellationToken))
        {
            matches.Add(match);
            if (matches.Count >= 2)
            {
                break; // We only need to know if there are 2+ matches
            }
        }

        _logger.LogDebug(
            "Conditional update search returned {MatchCount} matches for {ResourceType}",
            matches.Count,
            request.ResourceType);

        // 6. Handle 0/1/multiple matches
        if (matches.Count == 0)
        {
            // 0 matches: Create new resource (server-assigned ID)
            _logger.LogInformation(
                "Conditional update: 0 matches, creating new {ResourceType}",
                request.ResourceType);

            var resource = await CreateNewResourceAsync(
                request.ResourceType,
                request.JsonNode,
                request.TenantId,
                request.ProvenanceResource,
                cancellationToken);

            return new ConditionalUpdateResult(
                Resource: resource,
                WasCreated: true,
                MatchCount: 0);
        }
        else if (matches.Count == 1)
        {
            // 1 match: Update existing resource (preserve existing ID)
            var existingEntry = matches[0];
            var existingId = existingEntry.ResourceId;
            var existingVersionId = existingEntry.VersionId;

            _logger.LogInformation(
                "Conditional update: 1 match found, updating existing {ResourceType}/{Id} (version {VersionId})",
                existingEntry.ResourceType,
                existingId,
                existingVersionId);

            var resource = await UpdateExistingResourceAsync(
                request.ResourceType,
                existingId,
                existingVersionId,
                request.JsonNode,
                request.TenantId,
                request.ProvenanceResource,
                cancellationToken);

            return new ConditionalUpdateResult(
                Resource: resource,
                WasCreated: false,
                MatchCount: 1);
        }
        else
        {
            // Multiple matches - return 412 Precondition Failed
            _logger.LogWarning(
                "Conditional update: {Count} matches found, expected 0 or 1. Search criteria: {SearchCriteria}",
                matches.Count,
                request.SearchCriteria);

            throw new ConditionalOperationException(
                operation: "ConditionalUpdate",
                message: $"Multiple resources match the search criteria '{request.SearchCriteria}'. " +
                         $"Found {matches.Count} matches. Conditional update requires 0 or 1 match.",
                matchCount: matches.Count,
                searchCriteria: request.SearchCriteria);
        }
    }

    /// <summary>
    /// Creates a new resource from the parsed JSON.
    /// Used when 0 matches are found (conditional update creates new resource).
    /// FHIR Spec: Use client-provided ID if present, otherwise server-assigned.
    /// </summary>
    private async Task<ResourceWrapper> CreateNewResourceAsync(
        string resourceType,
        ResourceJsonNode jsonNode,
        int tenantId,
        ProvenanceJsonNode? provenanceResource,
        CancellationToken cancellationToken)
    {
        // Use client-provided ID if present, otherwise generate server-assigned ID
        string newId;
        if (!string.IsNullOrEmpty(jsonNode.Id))
        {
            // Client provided ID - use it for creation
            newId = jsonNode.Id;
            _logger.LogDebug(
                "Creating new {ResourceType} with client-provided ID: {Id}",
                resourceType,
                newId);
        }
        else
        {
            // No client ID - generate server-assigned ID
            newId = Guid.NewGuid().ToString("N");
            _logger.LogDebug(
                "Creating new {ResourceType} with server-assigned ID: {Id}",
                resourceType,
                newId);

            // Set the ID on the JsonNode since we're using PUT (not POST)
            // CreateOrUpdateResourceHandler only sets ID for POST, but conditional update uses PUT
            jsonNode.Id = newId;
        }

        // Create resource via CreateOrUpdateResourceHandler
        var createCommand = new CreateOrUpdateResourceCommand(
            ResourceType: resourceType,
            Id: newId,
            JsonNode: jsonNode,
            HttpMethod: System.Net.Http.HttpMethod.Put,
            Coordinator: null, // No bundle context for conditional update
            ProvenanceResource: provenanceResource);

        var updateResult = await _mediator.SendAsync(createCommand, cancellationToken);

        // Convert UpdateResult to SearchEntryResult for ConvertSearchEntryToWrapper
        var createdEntry = new SearchEntryResult(
            ResourceType: updateResult.Key.ResourceType,
            ResourceId: updateResult.Key.Id,
            VersionId: updateResult.Key.VersionId ?? "1",
            LastModified: updateResult.LastModified,
            ResourceBytes: updateResult.ResourceBytes.ToArray())
        {
            TenantId = updateResult.Key.TenantId,
            Request = updateResult.Request
        };

        // Convert SearchEntryResult to ResourceWrapper
        return ConvertSearchEntryToWrapper(createdEntry);
    }

    /// <summary>
    /// Updates an existing resource from the parsed JSON.
    /// Used when 1 match is found (conditional update updates existing resource).
    /// FHIR Spec: If body contains an ID that differs from the matched resource, return 412 Precondition Failed.
    /// IMPORTANT: Sets If-Match header with existing version ID to prevent lost updates (optimistic concurrency control).
    /// </summary>
    private async Task<ResourceWrapper> UpdateExistingResourceAsync(
        string resourceType,
        string existingId,
        string existingVersionId,
        ResourceJsonNode jsonNode,
        int tenantId,
        ProvenanceJsonNode? provenanceResource,
        CancellationToken cancellationToken)
    {
        // FHIR Spec: If client provided ID differs from existing ID, return 400 BadRequest
        // This is a malformed request, not a precondition failure
        if (!string.IsNullOrEmpty(jsonNode.Id) && jsonNode.Id != existingId)
        {
            _logger.LogWarning(
                "Conditional update: Client-provided ID '{ClientId}' differs from existing ID '{ExistingId}' - returning 400 BadRequest",
                jsonNode.Id,
                existingId);

            throw new Domain.Exceptions.BadRequestException(
                $"Resource ID in body ('{jsonNode.Id}') does not match the ID of the existing resource ('{existingId}'). " +
                "Cannot update resource with different ID.");
        }

        _logger.LogDebug(
            "Updating existing {ResourceType}/{Id} with version check (If-Match: {VersionId})",
            resourceType,
            existingId,
            existingVersionId);

        // Update resource via CreateOrUpdateResourceHandler (uses existing ID)
        // Pass IfMatch parameter for optimistic concurrency control
        // This prevents lost updates if someone modified the resource between search and update
        var updateCommand = new CreateOrUpdateResourceCommand(
            ResourceType: resourceType,
            Id: existingId, // Use existing ID, ignore body ID
            JsonNode: jsonNode,
            HttpMethod: System.Net.Http.HttpMethod.Put,
            Coordinator: null, // No bundle context for conditional update
            IfMatch: existingVersionId, // Pass version ID for optimistic concurrency control
            ProvenanceResource: provenanceResource);

        var updateResult = await _mediator.SendAsync(updateCommand, cancellationToken);

        // Convert UpdateResult to SearchEntryResult for ConvertSearchEntryToWrapper
        var updatedEntry = new SearchEntryResult(
            ResourceType: updateResult.Key.ResourceType,
            ResourceId: updateResult.Key.Id,
            VersionId: updateResult.Key.VersionId ?? "1",
            LastModified: updateResult.LastModified,
            ResourceBytes: updateResult.ResourceBytes.ToArray())
        {
            TenantId = updateResult.Key.TenantId,
            Request = updateResult.Request
        };

        // Convert SearchEntryResult to ResourceWrapper
        return ConvertSearchEntryToWrapper(updatedEntry);
    }

    /// <summary>
    /// Converts a SearchEntryResult (with raw bytes) to a ResourceWrapper.
    /// Parses the JSON bytes to create a ResourceJsonNode for the wrapper.
    /// </summary>
    private static ResourceWrapper ConvertSearchEntryToWrapper(SearchEntryResult entry)
    {
        // Parse the raw bytes directly (zero-copy) to ResourceJsonNode
        var jsonNode = JsonSourceNodeFactory.Parse(entry.ResourceBytes);

        var request = entry.Request ?? new ResourceRequest("PUT", $"{entry.ResourceType}/{entry.ResourceId}");

        return new ResourceWrapper(
            ResourceType: entry.ResourceType,
            ResourceId: entry.ResourceId,
            VersionId: entry.VersionId,
            LastModified: entry.LastModified,
            Resource: jsonNode,
            Request: request,
            IsDeleted: entry.IsDeleted)
        {
            TenantId = entry.TenantId
        };
    }
}
