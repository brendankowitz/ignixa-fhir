// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Expressions;

namespace Ignixa.DataLayer.SqlEntityFramework.Search;

/// <summary>
/// Processes _include expressions to fetch referenced resources.
/// Uses a post-query fetching approach: execute main query first, then fetch includes.
/// </summary>
public class IncludeProcessor
{
    private readonly FhirDbContext _context;
    private readonly SearchIndexReferenceDataCache _cache;
    private readonly IFhirRepository _repository;
    private readonly ILogger<IncludeProcessor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IncludeProcessor"/> class.
    /// </summary>
    /// <param name="context">The EF Core DbContext.</param>
    /// <param name="cache">The reference data cache.</param>
    /// <param name="repository">The repository for fetching full resources.</param>
    /// <param name="logger">Logger instance.</param>
    public IncludeProcessor(
        FhirDbContext context,
        SearchIndexReferenceDataCache cache,
        IFhirRepository repository,
        ILogger<IncludeProcessor> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Processes _include expressions and returns included resources.
    /// Returns raw bytes for zero-copy serialization.
    /// </summary>
    /// <param name="mainResults">The main search results to include from.</param>
    /// <param name="includeExpressions">The include expressions to process.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of included resources with raw bytes.</returns>
    public async Task<List<SearchEntryResult>> ProcessIncludesAsync(
        IReadOnlyList<SearchEntryResult> mainResults,
        IReadOnlyList<IncludeExpression> includeExpressions,
        CancellationToken ct)
    {
        if (mainResults.Count == 0 || includeExpressions.Count == 0)
        {
            return new List<SearchEntryResult>();
        }

        _logger.LogDebug("Processing {Count} _include expressions for {ResultCount} main results",
            includeExpressions.Count, mainResults.Count);

        var includedResources = new List<SearchEntryResult>();
        var processedResourceKeys = new HashSet<string>(); // Track to avoid duplicates

        foreach (var includeExpr in includeExpressions.Where(e => !e.Iterate && !e.Reversed))
        {
            var includes = await ProcessSingleIncludeAsync(mainResults, includeExpr, ct);

            foreach (var resource in includes)
            {
                var key = $"{resource.ResourceType}/{resource.ResourceId}";
                if (processedResourceKeys.Add(key))
                {
                    includedResources.Add(resource);
                }
            }
        }

        _logger.LogDebug("Found {IncludeCount} included resources", includedResources.Count);
        return includedResources;
    }

    /// <summary>
    /// Processes a single _include expression.
    /// </summary>
    private async Task<List<SearchEntryResult>> ProcessSingleIncludeAsync(
        IReadOnlyList<SearchEntryResult> mainResults,
        IncludeExpression includeExpr,
        CancellationToken ct)
    {
        _logger.LogDebug("Processing include: {Source} → {Parameter} → {Target}",
            includeExpr.SourceResourceType,
            includeExpr.ReferenceSearchParameter?.Code ?? "*",
            includeExpr.TargetResourceType ?? "*");

        // Step 1: Get source resource surrogate IDs
        var sourceResourceTypeId = await _cache.GetResourceTypeIdAsync(includeExpr.SourceResourceType);
        if (!sourceResourceTypeId.HasValue)
        {
            _logger.LogWarning("Source resource type not found: {Type}", includeExpr.SourceResourceType);
            return new List<SearchEntryResult>();
        }

        var sourceResourceIds = mainResults
            .Where(r => r.ResourceType == includeExpr.SourceResourceType)
            .Select(r => r.ResourceId)
            .ToList();

        if (sourceResourceIds.Count == 0)
        {
            return new List<SearchEntryResult>();
        }

        // Step 2: Find resource surrogate IDs for these resource IDs
        var sourceSurrogateIds = await _context.Resources
            .Where(r => r.ResourceTypeId == sourceResourceTypeId.Value
                && sourceResourceIds.Contains(r.ResourceId)
                && !r.IsHistory
                && !r.IsDeleted)
            .Select(r => r.ResourceSurrogateId)
            .ToListAsync(ct);

        if (sourceSurrogateIds.Count == 0)
        {
            return new List<SearchEntryResult>();
        }

        // Step 3: Extract reference targets from ReferenceSearchParam table
        List<(short ResourceTypeId, string ResourceId)> targetReferences;

        if (includeExpr.WildCard)
        {
            // Wildcard include: fetch all referenced resources
            targetReferences = await _context.ReferenceSearchParams
                .Where(rsp => sourceSurrogateIds.Contains(rsp.ResourceSurrogateId)
                    && rsp.ReferenceResourceTypeId != null)
                .Select(rsp => new ValueTuple<short, string>(
                    rsp.ReferenceResourceTypeId ?? 0,
                    rsp.ReferenceResourceId))
                .Distinct()
                .ToListAsync(ct);
        }
        else
        {
            // Specific reference parameter include
            // Get target resource type ID
            short? targetResourceTypeId = null;
            if (!string.IsNullOrEmpty(includeExpr.TargetResourceType))
            {
                targetResourceTypeId = await _cache.GetResourceTypeIdAsync(includeExpr.TargetResourceType);
            }

            // Query reference table filtered by target type if specified
            var referenceQuery = _context.ReferenceSearchParams
                .Where(rsp => sourceSurrogateIds.Contains(rsp.ResourceSurrogateId)
                    && rsp.ReferenceResourceTypeId != null);

            if (targetResourceTypeId.HasValue)
            {
                referenceQuery = referenceQuery.Where(rsp => rsp.ReferenceResourceTypeId == targetResourceTypeId.Value);
            }

            targetReferences = await referenceQuery
                .Select(rsp => new ValueTuple<short, string>(
                    rsp.ReferenceResourceTypeId ?? 0,
                    rsp.ReferenceResourceId))
                .Distinct()
                .ToListAsync(ct);
        }

        if (targetReferences.Count == 0)
        {
            _logger.LogDebug("No references found for include");
            return new List<SearchEntryResult>();
        }

        _logger.LogDebug("Found {Count} unique references to include", targetReferences.Count);

        // Step 4: Fetch target resources - return raw bytes for zero-copy serialization
        var includedResources = new List<SearchEntryResult>();

        // Group by resource type for efficient fetching
        var referencesByType = targetReferences.GroupBy(r => r.Item1);

        foreach (var typeGroup in referencesByType)
        {
            var resourceTypeId = typeGroup.Key;
            var resourceIds = typeGroup.Select(t => t.Item2).ToList();

            // Get resource type name
            var resourceTypeName = await GetResourceTypeNameAsync(resourceTypeId, ct);
            if (resourceTypeName == null)
            {
                continue;
            }

            // Fetch resources from repository - return raw bytes for zero-copy serialization
            foreach (var resourceId in resourceIds)
            {
                var key = new ResourceKey(resourceTypeName, resourceId);
                var searchResult = await _repository.GetAsync(key, ct);
                if (searchResult != null)
                {
                    includedResources.Add(searchResult);
                }
            }
        }

        return includedResources;
    }

    /// <summary>
    /// Gets the resource type name for a given resource type ID.
    /// </summary>
    private async Task<string?> GetResourceTypeNameAsync(short resourceTypeId, CancellationToken ct)
    {
        var resourceType = await _context.ResourceTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(rt => rt.ResourceTypeId == resourceTypeId, ct);

        return resourceType?.Name;
    }
}
