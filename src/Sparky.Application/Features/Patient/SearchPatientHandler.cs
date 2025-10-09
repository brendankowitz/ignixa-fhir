// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.Extensions.Logging;
using Sparky.Domain.Abstractions;

namespace Sparky.Application.Features.Patient;

/// <summary>
/// Handler for SearchPatientQuery.
/// </summary>
public class SearchPatientHandler : IRequestHandler<SearchPatientQuery, SearchPatientResult>
{
    private readonly ISearchService _searchService;
    private readonly ILogger<SearchPatientHandler> _logger;

    public SearchPatientHandler(
        ISearchService searchService,
        ILogger<SearchPatientHandler> logger)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<SearchPatientResult> HandleAsync(
        SearchPatientQuery request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Searching for Patient resources (streaming)");

        // Use streaming search for memory-efficient processing
        var resourceStream = _searchService.SearchStreamAsync(request.SearchOptions, cancellationToken);

        // TODO: Calculate total count if requested (Phase 1.2a)
        // Note: Calculating total with streaming requires either:
        // 1. Separate count query (recommended)
        // 2. Buffering all results (defeats streaming purpose)
        // 3. Return null total (current approach)
        int? total = null;
        if (request.SearchOptions.Total != Search.Models.TotalType.None)
        {
            // For now, return null - will implement separate count query in Phase 1.2a
            _logger.LogWarning("Total count requested but not yet supported with streaming");
            total = null;
        }

        var result = new SearchPatientResult(
            Resources: resourceStream,
            Total: total,
            ContinuationToken: null); // TODO: Implement paging in Phase 1.2a

        return Task.FromResult(result);
    }
}
