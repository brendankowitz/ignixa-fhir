// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.Extensions.Logging;
using Sparky.Domain.Abstractions;
using Sparky.Search.Models;

namespace Sparky.Application.Features.Resource;

/// <summary>
/// Generic handler for searching any FHIR resource type.
/// Replaces resource-specific handlers like SearchPatientHandler.
/// </summary>
public class SearchResourcesHandler : IRequestHandler<SearchResourcesQuery, SearchResourcesResult>
{
    private readonly ISearchService _searchService;
    private readonly ILogger<SearchResourcesHandler> _logger;

    public SearchResourcesHandler(
        ISearchService searchService,
        ILogger<SearchResourcesHandler> logger)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<SearchResourcesResult> HandleAsync(
        SearchResourcesQuery request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Searching for {ResourceType} resources (streaming)", request.ResourceType);

        // Use streaming search for memory-efficient processing
        var resourceStream = _searchService.SearchStreamAsync(request.SearchOptions, cancellationToken);

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
