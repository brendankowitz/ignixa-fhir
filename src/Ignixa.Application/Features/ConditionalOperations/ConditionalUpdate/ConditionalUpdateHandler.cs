// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.SourceNodeSerialization;
using Ignixa.SourceNodeSerialization.SourceNodes;

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
    private readonly RecyclableMemoryStreamManager _memoryStreamManager;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ConditionalUpdateHandler> _logger;

    public ConditionalUpdateHandler(
        IMediator mediator,
        IFhirRepositoryFactory repositoryFactory,
        IQueryParameterParser queryParser,
        ISearchOptionsBuilderFactory searchOptionsBuilderFactory,
        RecyclableMemoryStreamManager memoryStreamManager,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ConditionalUpdateHandler> logger)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _queryParser = queryParser ?? throw new ArgumentNullException(nameof(queryParser));
        _searchOptionsBuilderFactory = searchOptionsBuilderFactory ?? throw new ArgumentNullException(nameof(searchOptionsBuilderFactory));
        _memoryStreamManager = memoryStreamManager ?? throw new ArgumentNullException(nameof(memoryStreamManager));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ConditionalUpdateResult> HandleAsync(
        ConditionalUpdateCommand request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Processing conditional update for {ResourceType} with search criteria: {SearchCriteria}",
            request.ResourceType,
            request.SearchCriteria);

        // 1. Parse search criteria query string
        var queryParameters = _queryParser.Parse(request.SearchCriteria);

        // 2. Get FHIR version from context
        var fhirVersion = FhirVersionExtractor.ExtractFhirVersion(_httpContextAccessor.HttpContext);
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
                request.RequestBody,
                request.TenantId,
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
                request.RequestBody,
                request.TenantId,
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
    /// Creates a new resource from the request body.
    /// Used when 0 matches are found (conditional update creates new resource).
    /// </summary>
    private async Task<ResourceWrapper> CreateNewResourceAsync(
        string resourceType,
        string requestBody,
        int tenantId,
        CancellationToken cancellationToken)
    {
        // Parse request body to ResourceJsonNode
        ResourceJsonNode jsonNode;
        await using (var memoryStream = _memoryStreamManager.GetStream("conditional-update-request"))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(requestBody);
            await memoryStream.WriteAsync(bytes, cancellationToken);
            memoryStream.Position = 0;
            jsonNode = await JsonSourceNodeFactory.Parse(memoryStream);
        }

        // Generate new ID (server-assigned)
        var newId = Guid.NewGuid().ToString("N");

        _logger.LogDebug(
            "Creating new {ResourceType} with server-assigned ID: {Id}",
            resourceType,
            newId);

        // Create resource via CreateOrUpdateResourceHandler
        var createCommand = new CreateOrUpdateResourceCommand(
            ResourceType: resourceType,
            Id: newId,
            JsonNode: jsonNode,
            Coordinator: null); // No bundle context for conditional update

        var resourceKey = await _mediator.SendAsync(createCommand, cancellationToken);

        // Get the created resource to return
        var repository = await _repositoryFactory.GetRepositoryAsync(tenantId, cancellationToken);
        var createdEntry = await repository.GetAsync(resourceKey, cancellationToken);

        if (createdEntry == null)
        {
            throw new InvalidOperationException($"Failed to retrieve created resource {resourceType}/{resourceKey.Id}");
        }

        // Convert SearchEntryResult to ResourceWrapper
        return ConvertSearchEntryToWrapper(createdEntry);
    }

    /// <summary>
    /// Updates an existing resource from the request body.
    /// Used when 1 match is found (conditional update updates existing resource).
    /// Client-provided ID in body is ignored - existing ID is preserved.
    /// IMPORTANT: Sets If-Match header with existing version ID to prevent lost updates (optimistic concurrency control).
    /// </summary>
    private async Task<ResourceWrapper> UpdateExistingResourceAsync(
        string resourceType,
        string existingId,
        string existingVersionId,
        string requestBody,
        int tenantId,
        CancellationToken cancellationToken)
    {
        // Parse request body to ResourceJsonNode
        ResourceJsonNode jsonNode;
        await using (var memoryStream = _memoryStreamManager.GetStream("conditional-update-request"))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(requestBody);
            await memoryStream.WriteAsync(bytes, cancellationToken);
            memoryStream.Position = 0;
            jsonNode = await JsonSourceNodeFactory.Parse(memoryStream);
        }

        // Log warning if client provided ID differs from existing ID
        if (!string.IsNullOrEmpty(jsonNode.Id) && jsonNode.Id != existingId)
        {
            _logger.LogWarning(
                "Conditional update: Client-provided ID '{ClientId}' ignored, using existing ID '{ExistingId}'",
                jsonNode.Id,
                existingId);
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
            Coordinator: null, // No bundle context for conditional update
            IfMatch: existingVersionId); // Pass version ID for optimistic concurrency control

        var resourceKey = await _mediator.SendAsync(updateCommand, cancellationToken);

        // Get the updated resource to return
        var repository = await _repositoryFactory.GetRepositoryAsync(tenantId, cancellationToken);
        var updatedEntry = await repository.GetAsync(resourceKey, cancellationToken);

        if (updatedEntry == null)
        {
            throw new InvalidOperationException($"Failed to retrieve updated resource {resourceType}/{resourceKey.Id}");
        }

        // Convert SearchEntryResult to ResourceWrapper
        return ConvertSearchEntryToWrapper(updatedEntry);
    }

    /// <summary>
    /// Converts a SearchEntryResult (with raw bytes) to a ResourceWrapper.
    /// Parses the JSON bytes to create a ResourceJsonNode for the wrapper.
    /// </summary>
    private static ResourceWrapper ConvertSearchEntryToWrapper(SearchEntryResult entry)
    {
        // Parse the raw bytes to ResourceJsonNode using synchronous Parse(string) method
        var json = System.Text.Encoding.UTF8.GetString(entry.ResourceBytes.Span);
        var jsonNode = JsonSourceNodeFactory.Parse(json);

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
