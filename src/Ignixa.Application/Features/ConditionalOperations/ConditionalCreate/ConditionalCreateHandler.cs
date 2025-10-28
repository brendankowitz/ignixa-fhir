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

namespace Ignixa.Application.Features.ConditionalOperations.ConditionalCreate;

/// <summary>
/// Handles conditional create operations via If-None-Exist header.
/// FHIR R4 Section 3.1.0.5: Conditional Create
///
/// Behavior:
/// - 0 matches: Create new resource, return 201 Created
/// - 1 match: Return existing resource, return 200 OK (idempotent)
/// - Multiple matches: Return 412 Precondition Failed
/// </summary>
public class ConditionalCreateHandler : IRequestHandler<ConditionalCreateCommand, ConditionalCreateResult>
{
    private readonly IMediator _mediator;
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly IQueryParameterParser _queryParser;
    private readonly ISearchOptionsBuilderFactory _searchOptionsBuilderFactory;
    private readonly RecyclableMemoryStreamManager _memoryStreamManager;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ConditionalCreateHandler> _logger;

    public ConditionalCreateHandler(
        IMediator mediator,
        IFhirRepositoryFactory repositoryFactory,
        IQueryParameterParser queryParser,
        ISearchOptionsBuilderFactory searchOptionsBuilderFactory,
        RecyclableMemoryStreamManager memoryStreamManager,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ConditionalCreateHandler> logger)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _queryParser = queryParser ?? throw new ArgumentNullException(nameof(queryParser));
        _searchOptionsBuilderFactory = searchOptionsBuilderFactory ?? throw new ArgumentNullException(nameof(searchOptionsBuilderFactory));
        _memoryStreamManager = memoryStreamManager ?? throw new ArgumentNullException(nameof(memoryStreamManager));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ConditionalCreateResult> HandleAsync(
        ConditionalCreateCommand request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Processing conditional create for {ResourceType} with search criteria: {SearchCriteria}",
            request.ResourceType,
            request.IfNoneExist);

        // 1. Parse If-None-Exist query string
        var queryParameters = _queryParser.Parse(request.IfNoneExist);

        // Validate that search criteria is selective enough (has at least one parameter)
        if (queryParameters.Count == 0)
        {
            _logger.LogWarning(
                "Conditional create rejected: search criteria not selective enough for {ResourceType}",
                request.ResourceType);
            throw new Domain.Exceptions.BadRequestException(
                string.Format(Search.Resources.ConditionalOperationNotSelectiveEnough, request.ResourceType));
        }

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
            "Conditional create search returned {MatchCount} matches for {ResourceType}",
            matches.Count,
            request.ResourceType);

        // 6. Handle 0/1/multiple matches
        if (matches.Count == 0)
        {
            // Create new resource
            _logger.LogInformation(
                "Conditional create: 0 matches, creating new {ResourceType}",
                request.ResourceType);

            var resource = await CreateNewResourceAsync(
                request.ResourceType,
                request.JsonNode,
                request.TenantId,
                cancellationToken);

            return new ConditionalCreateResult(
                Resource: resource,
                WasCreated: true,
                MatchCount: 0);
        }
        else if (matches.Count == 1)
        {
            // Return existing resource
            var existingEntry = matches[0];

            _logger.LogInformation(
                "Conditional create: 1 match found, returning existing {ResourceType}/{Id}",
                existingEntry.ResourceType,
                existingEntry.ResourceId);

            // Get the full resource wrapper from repository
            var repository = await _repositoryFactory.GetRepositoryAsync(request.TenantId, cancellationToken);
            var resourceKey = new ResourceKey(existingEntry.ResourceType, existingEntry.ResourceId, existingEntry.VersionId);
            var existingResource = await repository.GetAsync(resourceKey, cancellationToken);

            if (existingResource == null)
            {
                // Race condition: resource was deleted between search and get
                _logger.LogWarning(
                    "Resource {ResourceType}/{Id} found in search but not in repository (deleted?)",
                    existingEntry.ResourceType,
                    existingEntry.ResourceId);

                // Treat as 0 matches and create new resource
                var resource = await CreateNewResourceAsync(
                    request.ResourceType,
                    request.JsonNode,
                    request.TenantId,
                    cancellationToken);

                return new ConditionalCreateResult(
                    Resource: resource,
                    WasCreated: true,
                    MatchCount: 0);
            }

            // Convert SearchEntryResult to ResourceWrapper
            // We need to fetch the full ResourceWrapper, but IFhirRepository only returns SearchEntryResult
            // For now, we'll create a lightweight wrapper using the search result data
            var existingWrapper = ConvertSearchEntryToWrapper(existingEntry);

            return new ConditionalCreateResult(
                Resource: existingWrapper,
                WasCreated: false,
                MatchCount: 1);
        }
        else
        {
            // Multiple matches - return 412 Precondition Failed
            _logger.LogWarning(
                "Conditional create: {Count} matches found, expected 0 or 1. Search criteria: {SearchCriteria}",
                matches.Count,
                request.IfNoneExist);

            throw new ConditionalOperationException(
                operation: "ConditionalCreate",
                message: $"Multiple resources match the search criteria '{request.IfNoneExist}'. " +
                         $"Found {matches.Count} matches. Conditional create requires 0 or 1 match.",
                matchCount: matches.Count,
                searchCriteria: request.IfNoneExist);
        }
    }

    /// <summary>
    /// Creates a new resource from the request body.
    /// </summary>
    private async Task<ResourceWrapper> CreateNewResourceAsync(
        string resourceType,
        ResourceJsonNode jsonNode,
        int tenantId,
        CancellationToken cancellationToken)
    {
        // Check if we're in a bundle context with a pre-assigned ID
        // This is used for bundles with urn:uuid references to maintain referential integrity
        string? bundleAssignedId = null;
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null && httpContext.Items.TryGetValue("BundleAssignedResourceId", out var assignedIdObj))
        {
            bundleAssignedId = assignedIdObj as string;
        }

        // Generate new ID (server-assigned, or use bundle-assigned ID if in bundle context)
        var newId = bundleAssignedId ?? Guid.NewGuid().ToString("N");

        _logger.LogDebug(
            "Creating new {ResourceType} with {IdType}ID: {Id}",
            resourceType,
            bundleAssignedId != null ? "bundle-assigned " : "server-assigned ",
            newId);

        // Create resource via CreateOrUpdateResourceHandler
        var createCommand = new CreateOrUpdateResourceCommand(
            ResourceType: resourceType,
            Id: newId,
            JsonNode: jsonNode,
            HttpMethod: System.Net.Http.HttpMethod.Post,
            Coordinator: null); // No bundle context for conditional create

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
    /// Converts a SearchEntryResult (with raw bytes) to a ResourceWrapper.
    /// Parses the JSON bytes to create a ResourceJsonNode for the wrapper.
    /// </summary>
    private static ResourceWrapper ConvertSearchEntryToWrapper(SearchEntryResult entry)
    {
        // Parse the raw bytes directly (zero-copy) to ResourceJsonNode
        var jsonNode = JsonSourceNodeFactory.Parse(entry.ResourceBytes);

        var request = entry.Request ?? new ResourceRequest("GET", $"{entry.ResourceType}/{entry.ResourceId}");

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
