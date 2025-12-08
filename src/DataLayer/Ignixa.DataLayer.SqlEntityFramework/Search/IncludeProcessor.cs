// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ignixa.DataLayer.SqlEntityFramework.Compression;
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
    private readonly GzipResourceCompressor _compressor;
    private readonly ILogger<IncludeProcessor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IncludeProcessor"/> class.
    /// </summary>
    /// <param name="context">The EF Core DbContext.</param>
    /// <param name="cache">The reference data cache.</param>
    /// <param name="compressor">The resource compressor for decompressing RawResource bytes.</param>
    /// <param name="logger">Logger instance.</param>
    public IncludeProcessor(
        FhirDbContext context,
        SearchIndexReferenceDataCache cache,
        GzipResourceCompressor compressor,
        ILogger<IncludeProcessor> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _compressor = compressor ?? throw new ArgumentNullException(nameof(compressor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Processes _include expressions and returns included resources.
    /// Returns raw bytes for zero-copy serialization.
    /// </summary>
    /// <param name="sourceResourceIdentities">The resource identities (type + id) to find references from.</param>
    /// <param name="includeExpressions">The include expressions to process.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="forIteration">When true, processes iterate expressions instead of filtering them out.
    /// Used by IterateProcessor to process :iterate modifiers.</param>
    /// <returns>A list of included resources with raw bytes.</returns>
    public async Task<List<SearchEntryResult>> ProcessIncludesAsync(
        IReadOnlyList<(string ResourceType, string ResourceId)> sourceResourceIdentities,
        IReadOnlyList<IncludeExpression> includeExpressions,
        CancellationToken ct,
        bool forIteration = false)
    {
        if (sourceResourceIdentities.Count == 0 || includeExpressions.Count == 0)
        {
            return new List<SearchEntryResult>();
        }

        _logger.LogDebug("Processing {Count} _include expressions for {ResultCount} main results (forIteration={ForIteration})",
            includeExpressions.Count, sourceResourceIdentities.Count, forIteration);

        var includedResources = new List<SearchEntryResult>();
        var processedResourceKeys = new HashSet<string>(); // Track to avoid duplicates

        // When forIteration is true, process iterate expressions; otherwise filter them out
        // Both modes filter out reversed expressions (those are handled by RevIncludeProcessor)
        foreach (var includeExpr in includeExpressions.Where(e => e.Iterate == forIteration && !e.Reversed))
        {
            var includes = await ProcessSingleIncludeAsync(sourceResourceIdentities, includeExpr, ct);

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
        IReadOnlyList<(string ResourceType, string ResourceId)> sourceResourceIdentities,
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

        var sourceResourceIds = sourceResourceIdentities
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

            // Fetch all resources for this type in a single query (not N queries)
            var resourceEntities = await _context.Resources
                .Where(r => r.ResourceTypeId == resourceTypeId
                    && resourceIds.Contains(r.ResourceId)
                    && !r.IsHistory
                    && !r.IsDeleted)
                .Include(r => r.Transaction)
                .ToListAsync(ct);

            // Map entities directly to SearchEntryResult
            foreach (var entity in resourceEntities)
            {
                var result = new SearchEntryResult(
                    ResourceType: resourceTypeName,
                    ResourceId: entity.ResourceId,
                    VersionId: entity.Version.ToString(),
                    LastModified: entity.Transaction?.CreateDate ?? DateTimeOffset.UtcNow,
                    ResourceBytes: _compressor.DecompressBytes(entity.RawResource))
                {
                    IsDeleted = entity.IsDeleted,
                    SearchMode = SearchEntryMode.Include,
                };
                includedResources.Add(result);
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
