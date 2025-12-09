// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ignixa.DataLayer.SqlEntityFramework.Compression;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Expressions;

namespace Ignixa.DataLayer.SqlEntityFramework.Search;

/// <summary>
/// Processes _revinclude expressions to fetch resources that reference the main results.
/// Uses a post-query fetching approach: execute main query first, then fetch reverse includes.
/// </summary>
public class RevIncludeProcessor
{
    private readonly FhirDbContext _context;
    private readonly SearchIndexReferenceDataCache _cache;
    private readonly GzipResourceCompressor _compressor;
    private readonly ILogger<RevIncludeProcessor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RevIncludeProcessor"/> class.
    /// </summary>
    /// <param name="context">The EF Core DbContext.</param>
    /// <param name="cache">The reference data cache.</param>
    /// <param name="compressor">The resource compressor for decompressing RawResource bytes.</param>
    /// <param name="logger">Logger instance.</param>
    public RevIncludeProcessor(
        FhirDbContext context,
        SearchIndexReferenceDataCache cache,
        GzipResourceCompressor compressor,
        ILogger<RevIncludeProcessor> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _compressor = compressor ?? throw new ArgumentNullException(nameof(compressor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Processes _revinclude expressions and returns resources that reference the target resources.
    /// </summary>
    /// <param name="targetResourceIdentities">The resource identities (type + id) to find reverse references to.</param>
    /// <param name="revIncludeExpressions">The revinclude expressions to process.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="forIteration">When true, processes iterate expressions instead of filtering them out.
    /// Used by IterateProcessor to process :iterate modifiers.</param>
    /// <returns>A list of resources that reference the target resources.</returns>
    public async Task<List<SearchEntryResult>> ProcessRevIncludesAsync(
        IReadOnlyList<(string ResourceType, string ResourceId)> targetResourceIdentities,
        IReadOnlyList<IncludeExpression> revIncludeExpressions,
        CancellationToken ct,
        bool forIteration = false)
    {
        if (targetResourceIdentities.Count == 0 || revIncludeExpressions.Count == 0)
        {
            return new List<SearchEntryResult>();
        }

        _logger.LogDebug("Processing {Count} _revinclude expressions for {ResultCount} main results (forIteration={ForIteration})",
            revIncludeExpressions.Count, targetResourceIdentities.Count, forIteration);

        var revIncludedResources = new List<SearchEntryResult>();
        var processedResourceKeys = new HashSet<string>(); // Track to avoid duplicates

        // When forIteration is true, process iterate expressions; otherwise filter them out
        // Both modes require Reversed = true (this is for _revinclude)
        foreach (var revIncludeExpr in revIncludeExpressions.Where(e => e.Iterate == forIteration && e.Reversed))
        {
            var revIncludes = await ProcessSingleRevIncludeAsync(targetResourceIdentities, revIncludeExpr, ct);

            foreach (var resource in revIncludes)
            {
                var key = $"{resource.ResourceType}/{resource.ResourceId}";
                if (processedResourceKeys.Add(key))
                {
                    revIncludedResources.Add(resource);
                }
            }
        }

        _logger.LogDebug("Found {RevIncludeCount} reverse included resources", revIncludedResources.Count);
        return revIncludedResources;
    }

    /// <summary>
    /// Processes a single _revinclude expression.
    /// </summary>
    /// <remarks>
    /// For _revinclude, we need to find resources that reference the target resources.
    /// Supports three patterns:
    /// - Specific: _revinclude=Type:param (specific source type and search parameter)
    /// - Wildcard param: _revinclude=Type:* (specific source type, any search parameter)
    /// - Wildcard source: _revinclude=*:* (any source type, any search parameter)
    ///
    /// Example: Patient?_revinclude=Observation:patient
    ///   - Target results: Patients
    ///   - SourceResourceType: Observation (the type that references)
    ///   - TargetResourceType: Patient (the target search type)
    ///   - Find Observations that reference these Patients
    /// </remarks>
    private async Task<List<SearchEntryResult>> ProcessSingleRevIncludeAsync(
        IReadOnlyList<(string ResourceType, string ResourceId)> targetResourceIdentities,
        IncludeExpression revIncludeExpr,
        CancellationToken ct)
    {
        _logger.LogDebug("Processing revinclude: {Source} ← {Parameter} ← {Target}",
            revIncludeExpr.TargetResourceType ?? "*",
            revIncludeExpr.ReferenceSearchParameter?.Code ?? "*",
            revIncludeExpr.SourceResourceType);

        // Check for wildcard source type (*:*) - find ALL resources of ANY type that reference main results
        bool isWildcardSource = revIncludeExpr.SourceResourceType == "*";

        if (isWildcardSource)
        {
            return await ProcessWildcardSourceRevIncludeAsync(targetResourceIdentities, ct);
        }

        // Step 1: Get target resource type ID (the main search resource type)
        // For _revinclude, TargetResourceType may be null. Use Requires to get the actual target type(s).
        // Requires returns the resource types that the revinclude depends on (i.e., the main search results).
        string targetResourceTypeName;
        if (!string.IsNullOrEmpty(revIncludeExpr.TargetResourceType))
        {
            targetResourceTypeName = revIncludeExpr.TargetResourceType;
        }
        else if (revIncludeExpr.Requires?.Any() == true)
        {
            // For _revinclude without explicit target type, Requires contains the main search resource type(s)
            targetResourceTypeName = revIncludeExpr.Requires.First();
        }
        else
        {
            _logger.LogWarning("Cannot determine target resource type for revinclude expression");
            return [];
        }

        var targetResourceTypeId = await _cache.GetResourceTypeIdAsync(targetResourceTypeName);
        if (!targetResourceTypeId.HasValue)
        {
            _logger.LogWarning("Target resource type not found: {Type}", targetResourceTypeName);
            return [];
        }

        // Step 2: Get target resource IDs that we want to find references to
        var targetResourceIds = targetResourceIdentities
            .Where(r => r.ResourceType == targetResourceTypeName)
            .Select(r => r.ResourceId)
            .ToList();

        if (targetResourceIds.Count == 0)
        {
            return [];
        }

        // Step 3: Find resource surrogate IDs for target resources
        var targetSurrogateIds = await _context.Resources
            .Where(r => r.ResourceTypeId == targetResourceTypeId.Value
                && targetResourceIds.Contains(r.ResourceId)
                && !r.IsHistory
                && !r.IsDeleted)
            .Select(r => r.ResourceSurrogateId)
            .ToListAsync(ct);

        if (targetSurrogateIds.Count == 0)
        {
            return [];
        }

        // Step 4: Get source resource type ID (the type that references the main results)
        var sourceResourceTypeId = await _cache.GetResourceTypeIdAsync(revIncludeExpr.SourceResourceType);
        if (!sourceResourceTypeId.HasValue)
        {
            _logger.LogWarning("Source resource type not found: {Type}", revIncludeExpr.SourceResourceType);
            return [];
        }

        // Step 4.5: Get SearchParamId for the reference search parameter
        short? searchParamId = null;
        if (revIncludeExpr.ReferenceSearchParameter?.Url is not null)
        {
            searchParamId = await _cache.GetSearchParamIdAsync(revIncludeExpr.ReferenceSearchParameter);
            if (!searchParamId.HasValue)
            {
                _logger.LogWarning("SearchParamId not found for: {Url}", revIncludeExpr.ReferenceSearchParameter.Url);
                return [];
            }

            _logger.LogDebug("Using SearchParamId {Id} for {Url}", searchParamId.Value, revIncludeExpr.ReferenceSearchParameter.Url);
        }

        // Step 5: Find references FROM source type TO target resources
        // Query: ReferenceSearchParams where:
        //   - ResourceTypeId = source type (e.g., Observation)
        //   - ReferenceResourceTypeId = target type (e.g., Patient)
        //   - SearchParamId = specific search parameter (e.g., Encounter-subject)
        //   - Join to resolve ReferenceResourceId to surrogate ID
        //   - Filter by target surrogate IDs (the main results we're finding references to)
        var referencingResourceIds = await _context.ReferenceSearchParams
            .Where(rsp => rsp.ResourceTypeId == sourceResourceTypeId.Value
                && rsp.ReferenceResourceTypeId == targetResourceTypeId.Value
                && (searchParamId == null || rsp.SearchParamId == searchParamId.Value))
            .Join(_context.Resources,
                rsp => new { ResourceTypeId = rsp.ReferenceResourceTypeId ?? (short)0, ResourceId = rsp.ReferenceResourceId },
                res => new { res.ResourceTypeId, res.ResourceId },
                (rsp, res) => new { rsp.ResourceSurrogateId, TargetSurrogateId = res.ResourceSurrogateId })
            .Where(joined => targetSurrogateIds.Contains(joined.TargetSurrogateId))
            .Select(joined => joined.ResourceSurrogateId)
            .Distinct()
            .ToListAsync(ct);

        if (referencingResourceIds.Count == 0)
        {
            _logger.LogDebug("No reverse references found");
            return [];
        }

        _logger.LogDebug("Found {Count} unique reverse references", referencingResourceIds.Count);

        // Step 6: Fetch the full referencing resource entities (not just IDs)
        // Include the Transaction relation for CreatedDate
        var referencingEntities = await _context.Resources
            .Where(r => referencingResourceIds.Contains(r.ResourceSurrogateId)
                && !r.IsHistory
                && !r.IsDeleted)
            .Include(r => r.Transaction)
            .Include(r => r.ResourceType)
            .ToListAsync(ct);

        // Map entities directly to SearchEntryResult (single query, not N+1)
        return MapEntitiesToSearchResults(referencingEntities, revIncludeExpr.SourceResourceType);
    }

    /// <summary>
    /// Processes wildcard source revinclude (_revinclude=*:*) - finds ALL resources of ANY type that reference main results.
    /// </summary>
    private async Task<List<SearchEntryResult>> ProcessWildcardSourceRevIncludeAsync(
        IReadOnlyList<(string ResourceType, string ResourceId)> targetResourceIdentities,
        CancellationToken ct)
    {
        _logger.LogDebug("Processing wildcard source revinclude (*:*) for {Count} target resources", targetResourceIdentities.Count);

        var stopwatch = Stopwatch.StartNew();

        if (targetResourceIdentities.Count == 0)
        {
            return [];
        }

        // Group targets by resource type for efficient lookup
        var targetsByType = targetResourceIdentities
            .GroupBy(t => t.ResourceType)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ResourceId).ToList());

        // Build a list of (ResourceTypeId, ResourceId) pairs for all targets
        var targetTypeIdPairs = new List<(short ResourceTypeId, string ResourceId)>();
        foreach (var (resourceType, resourceIds) in targetsByType)
        {
            var typeId = await _cache.GetResourceTypeIdAsync(resourceType);
            if (typeId.HasValue)
            {
                foreach (var resourceId in resourceIds)
                {
                    targetTypeIdPairs.Add((typeId.Value, resourceId));
                }
            }
        }

        if (targetTypeIdPairs.Count == 0)
        {
            return [];
        }

        // Get surrogate IDs for all target resources
        // OPTIMIZATION: Batch queries by resource type (one query per type instead of per resource)
        var targetSurrogateIds = new List<long>();
        foreach (var group in targetTypeIdPairs.GroupBy(p => p.ResourceTypeId))
        {
            var typeId = group.Key;
            var resourceIds = group.Select(p => p.ResourceId).ToList();

            var surrogateIds = await _context.Resources
                .Where(r => r.ResourceTypeId == typeId
                    && resourceIds.Contains(r.ResourceId)
                    && !r.IsHistory
                    && !r.IsDeleted)
                .Select(r => r.ResourceSurrogateId)
                .ToListAsync(ct);

            targetSurrogateIds.AddRange(surrogateIds);
        }

        _logger.LogDebug("Fetched {Count} target surrogate IDs in {ElapsedMs}ms",
            targetSurrogateIds.Count,
            stopwatch.ElapsedMilliseconds);

        if (targetSurrogateIds.Count == 0)
        {
            return [];
        }

        // Find ALL resources (any type) that reference any of the target resources
        // This is the key difference from specific revinclude - we don't filter by source resource type
        var referencingResourceIds = await _context.ReferenceSearchParams
            .Where(rsp => targetTypeIdPairs.Select(t => t.ResourceTypeId).Contains(rsp.ReferenceResourceTypeId ?? (short)0))
            .Join(_context.Resources,
                rsp => new { ResourceTypeId = rsp.ReferenceResourceTypeId ?? (short)0, ResourceId = rsp.ReferenceResourceId },
                res => new { res.ResourceTypeId, res.ResourceId },
                (rsp, res) => new { rsp.ResourceSurrogateId, TargetSurrogateId = res.ResourceSurrogateId })
            .Where(joined => targetSurrogateIds.Contains(joined.TargetSurrogateId))
            .Select(joined => joined.ResourceSurrogateId)
            .Distinct()
            .ToListAsync(ct);

        if (referencingResourceIds.Count == 0)
        {
            _logger.LogDebug("No wildcard reverse references found after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            return [];
        }

        _logger.LogDebug("Found {Count} unique wildcard reverse references in {ElapsedMs}ms",
            referencingResourceIds.Count,
            stopwatch.ElapsedMilliseconds);

        // Fetch the full referencing resource entities
        // IMPORTANT: Include ResourceType navigation for accurate type resolution
        var referencingEntities = await _context.Resources
            .Where(r => referencingResourceIds.Contains(r.ResourceSurrogateId)
                && !r.IsHistory
                && !r.IsDeleted)
            .Include(r => r.Transaction)
            .Include(r => r.ResourceType)
            .ToListAsync(ct);

        stopwatch.Stop();
        _logger.LogInformation("Wildcard revinclude completed: {ResultCount} resources in {ElapsedMs}ms",
            referencingEntities.Count,
            stopwatch.ElapsedMilliseconds);

        // Map entities to SearchEntryResult using the ResourceType navigation property
        return MapEntitiesToSearchResults(referencingEntities, sourceResourceType: null);
    }

    /// <summary>
    /// Maps resource entities to SearchEntryResult objects.
    /// </summary>
    /// <param name="entities">The resource entities to map.</param>
    /// <param name="sourceResourceType">The source resource type, or null if it should be resolved from entity.</param>
    private List<SearchEntryResult> MapEntitiesToSearchResults(IEnumerable<ResourceEntity> entities, string? sourceResourceType)
    {
        var results = new List<SearchEntryResult>();
        foreach (var entity in entities)
        {
            // If sourceResourceType is null (wildcard source), get the type from the entity's ResourceType navigation
            var resourceType = sourceResourceType ?? entity.ResourceType?.Name ?? "Unknown";

            var result = new SearchEntryResult(
                ResourceType: resourceType,
                ResourceId: entity.ResourceId,
                VersionId: entity.Version.ToString(),
                LastModified: entity.Transaction?.CreateDate ?? DateTimeOffset.UtcNow,
                ResourceBytes: _compressor.DecompressBytes(entity.RawResource))
            {
                IsDeleted = entity.IsDeleted,
                SearchMode = SearchEntryMode.Include,  // Mark as reverse-included resource
            };
            results.Add(result);
        }

        return results;
    }
}
