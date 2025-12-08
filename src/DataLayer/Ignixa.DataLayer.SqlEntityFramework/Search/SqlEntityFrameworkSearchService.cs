// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ignixa.DataLayer.SqlEntityFramework.Compression;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.DataLayer.SqlEntityFramework.Search;

/// <summary>
/// Entity Framework Core implementation of ISearchService for Microsoft FHIR Server legacy schema.
/// Translates FHIR search expressions into SQL queries using EF Core LINQ.
/// </summary>
public class SqlEntityFrameworkSearchService : ISearchService
{
    private readonly FhirDbContext _context;
    private readonly IFhirRepository _repository;
    private readonly SearchExpressionQueryBuilder _queryBuilder;
    private readonly IncludeProcessor _includeProcessor;
    private readonly RevIncludeProcessor _revIncludeProcessor;
    private readonly IterateProcessor _iterateProcessor;
    private readonly GzipResourceCompressor _compressor;
    private readonly ILogger<SqlEntityFrameworkSearchService> _logger;
    private readonly SearchIndexReferenceDataCache _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlEntityFrameworkSearchService"/> class.
    /// </summary>
    /// <param name="context">The EF Core DbContext.</param>
    /// <param name="repository">The repository for fetching full resources.</param>
    /// <param name="queryBuilder">The query builder for translating search expressions.</param>
    /// <param name="includeProcessor">The include processor for _include.</param>
    /// <param name="revIncludeProcessor">The revinclude processor for _revinclude.</param>
    /// <param name="iterateProcessor">The iterate processor for :iterate modifier.</param>
    /// <param name="compressor">The resource compressor for decompressing RawResource bytes.</param>
    /// <param name="cache">The search index reference data cache for looking up SearchParamIds.</param>
    /// <param name="logger">Logger instance.</param>
    public SqlEntityFrameworkSearchService(
        FhirDbContext context,
        IFhirRepository repository,
        SearchExpressionQueryBuilder queryBuilder,
        IncludeProcessor includeProcessor,
        RevIncludeProcessor revIncludeProcessor,
        IterateProcessor iterateProcessor,
        GzipResourceCompressor compressor,
        SearchIndexReferenceDataCache cache,
        ILogger<SqlEntityFrameworkSearchService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _queryBuilder = queryBuilder ?? throw new ArgumentNullException(nameof(queryBuilder));
        _includeProcessor = includeProcessor ?? throw new ArgumentNullException(nameof(includeProcessor));
        _revIncludeProcessor = revIncludeProcessor ?? throw new ArgumentNullException(nameof(revIncludeProcessor));
        _iterateProcessor = iterateProcessor ?? throw new ArgumentNullException(nameof(iterateProcessor));
        _compressor = compressor ?? throw new ArgumentNullException(nameof(compressor));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<SearchEntryResult> SearchStreamAsync<TSearchOptions>(
        TSearchOptions searchOptions,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        where TSearchOptions : class
    {
        // Cast to SearchOptions (we only support this type for now)
        if (searchOptions is not SearchOptions options)
        {
            throw new ArgumentException($"Search options must be of type {nameof(SearchOptions)}", nameof(searchOptions));
        }

        _logger.LogDebug("Streaming search for {ResourceType}", options.ResourceType ?? "<null - relying on expression>");

        // For wildcard/multi-type searches (ResourceType is null or empty), skip type lookup
        // and rely on the expression tree to filter results (e.g., Union with _type filters)
        if (string.IsNullOrEmpty(options.ResourceType))
        {
            _logger.LogDebug("Null/empty ResourceType detected - building query without resource type constraint");

            // Build query without resource type ID constraint (expression must handle filtering)
            var multiTypeQuery = await BuildQueryAsync(options, resourceTypeId: null, ct);

            _logger.LogInformation("Executing streaming query for multi-type search\n{SQL}",
                multiTypeQuery.ToQueryString());

            // Stream results from all matching resources - buffer for include processing
            var multiTypeMainResults = new List<SearchEntryResult>();
            await foreach (var entity in multiTypeQuery
                .Include(x => x.Transaction)
                .Include(x => x.ResourceType)
                .AsAsyncEnumerable().WithCancellation(ct))
            {
                // For multi-type results, we need to determine the actual resource type from the entity
                // entity.ResourceType is a ResourceTypeEntity, get the Name property
                if (entity.ResourceType == null)
                {
                    _logger.LogWarning("ResourceType is null for entity with ResourceId {ResourceId}", entity.ResourceId);
                    continue;
                }

                var searchResult = MapResourceEntityToSearchResult(entity, entity.ResourceType.Name);
                multiTypeMainResults.Add(searchResult);
                yield return searchResult;  // Stream immediately
            }

            // Process includes/revincludes if requested
            if (options.Include.Count > 0 || options.RevInclude.Count > 0)
            {
                var includedResources = await ProcessIncludesAndRevIncludesAsync(multiTypeMainResults, options, ct);
                foreach (var included in includedResources)
                {
                    // Mark included resources with Include mode instead of Match
                    yield return included with { SearchMode = SearchEntryMode.Include };
                }
            }
            yield break;
        }

        // Get ResourceTypeId for single-type searches
        var resourceTypeId = await GetResourceTypeIdAsync(options.ResourceType, ct);
        if (!resourceTypeId.HasValue)
        {
            _logger.LogWarning("ResourceType not found: {ResourceType}", options.ResourceType);
            yield break;
        }

        // Build query
        var query = await BuildQueryAsync(options, resourceTypeId.Value, ct);

        _logger.LogInformation("Executing streaming query for {ResourceType}\n{SQL}",
            options.ResourceType,
            query.ToQueryString());

        // Check if any iterate expressions exist - they require buffering results
        var hasIterateExpressions = options.Include.Any(e => e.Iterate) || options.RevInclude.Any(e => e.Iterate);

        // Phase 1: Stream main results (buffer if iterate expressions exist)
        var mainResults = new List<SearchEntryResult>();
        await foreach (var entity in query
            .Include(x => x.Transaction)
            .AsAsyncEnumerable().WithCancellation(ct))
        {
            var searchResult = MapResourceEntityToSearchResult(entity, options.ResourceType);
            if (hasIterateExpressions)
            {
                mainResults.Add(searchResult);
            }
            yield return searchResult;  // Stream immediately to client
        }

        // Phase 2: Process included resources (non-iterate only in streaming mode)
        // Filter to non-iterate expressions for first-level processing
        var nonIterateIncludes = options.Include.Where(e => !e.Iterate).ToList();
        var allFirstLevelIncludes = new List<SearchEntryResult>();

        if (nonIterateIncludes.Count > 0)
        {
            _logger.LogDebug("Processing {IncludeCount} non-iterate _include expressions", nonIterateIncludes.Count);

            foreach (var includeExpr in nonIterateIncludes)
            {
                var includeQuery = BuildIncludeQuery(options, includeExpr, resourceTypeId.Value);

                _logger.LogDebug("Executing include query for parameter {ParamCode}", includeExpr.ReferenceSearchParameter?.Code ?? "*");

                // Stream each included resource directly to client
                // For wildcard includes or unspecified target type, get resource type from navigation property or cache
                await foreach (var entity in includeQuery
                    .AsAsyncEnumerable().WithCancellation(ct))
                {
                    // Determine resource type: use target type if specified, otherwise get from entity or cache
                    var resourceTypeName = includeExpr.TargetResourceType
                        ?? entity.ResourceType?.Name
                        ?? _cache.TryGetResourceTypeNameFromCache(entity.ResourceTypeId)
                        ?? throw new InvalidOperationException($"ResourceType ID {entity.ResourceTypeId} not found in cache or database");

                    var searchResult = MapResourceEntityToSearchResult(entity, resourceTypeName);
                    searchResult = searchResult with { SearchMode = SearchEntryMode.Include };
                    if (hasIterateExpressions)
                    {
                        allFirstLevelIncludes.Add(searchResult);
                    }
                    yield return searchResult;
                }
            }

            _logger.LogDebug("Include processing completed");
        }

        // Phase 3: Process reverse-included resources (non-iterate only in streaming mode)
        // Filter to non-iterate expressions for first-level processing
        var nonIterateRevIncludes = options.RevInclude.Where(e => !e.Iterate).ToList();

        if (nonIterateRevIncludes.Count > 0)
        {
            _logger.LogDebug("Processing {RevIncludeCount} non-iterate _revinclude expressions", nonIterateRevIncludes.Count);

            foreach (var revIncludeExpr in nonIterateRevIncludes)
            {
                var revIncludeQuery = BuildRevIncludeQuery(options, revIncludeExpr, resourceTypeId.Value);

                _logger.LogDebug("Executing revinclude query for parameter {ParamCode} with {Sql}",
                    revIncludeExpr.ReferenceSearchParameter?.Code,
                    revIncludeQuery.ToQueryString());

                // Stream each reverse-included resource directly to client
                await foreach (var entity in revIncludeQuery
                    .AsAsyncEnumerable().WithCancellation(ct))
                {
                    // Determine resource type from entity
                    // First try using the provided SourceResourceType, then navigation property, then fall back to cache lookup of the ID
                    var resourceTypeName = revIncludeExpr.SourceResourceType
                        ?? entity.ResourceType?.Name
                        ?? _cache.TryGetResourceTypeNameFromCache(entity.ResourceTypeId)
                        ?? throw new InvalidOperationException($"ResourceType ID {entity.ResourceTypeId} not found in cache or database");

                    var searchResult = MapResourceEntityToSearchResult(entity, resourceTypeName);
                    searchResult = searchResult with { SearchMode = SearchEntryMode.Include };
                    if (hasIterateExpressions)
                    {
                        allFirstLevelIncludes.Add(searchResult);
                    }
                    yield return searchResult;
                }
            }

            _logger.LogDebug("RevInclude processing completed");
        }

        // Phase 4: Process :iterate expressions (recursive includes/revincludes)
        if (hasIterateExpressions)
        {
            var allIterateExpressions = options.Include
                .Concat(options.RevInclude)
                .Where(e => e.Iterate)
                .ToList();

            _logger.LogDebug("Processing {IterateCount} :iterate expressions", allIterateExpressions.Count);

            // Combine main results and first-level includes as starting point for iteration
            var iterationStartingPoint = mainResults.Concat(allFirstLevelIncludes).ToList();

            var iteratedResources = await _iterateProcessor.ProcessIteratesAsync(
                iterationStartingPoint,
                allIterateExpressions,
                ct);

            foreach (var resource in iteratedResources)
            {
                yield return resource with { SearchMode = SearchEntryMode.Include };
            }

            _logger.LogDebug("Added {IteratedCount} iterated resources", iteratedResources.Count);
        }
    }

    /// <inheritdoc/>
    public async ValueTask<int> CountAsync<TSearchOptions>(
        TSearchOptions searchOptions,
        CancellationToken ct = default)
        where TSearchOptions : class
    {
        // Cast to SearchOptions (we only support this type for now)
        if (searchOptions is not SearchOptions options)
        {
            throw new ArgumentException($"Search options must be of type {nameof(SearchOptions)}", nameof(searchOptions));
        }

        _logger.LogDebug("Counting resources for {ResourceType}", options.ResourceType ?? "all resource types");

        // For system-wide search (ResourceType is null), skip type lookup and query all resources
        if (string.IsNullOrEmpty(options.ResourceType))
        {
            _logger.LogDebug("System-wide count query - no resource type filter");

            IQueryable<ResourceEntity> multiTypeBaseQuery;

            // Handle _type parameter filtering for system-wide search
            if (options.ResourceTypes.Count > 0)
            {
                var resourceTypeNames = options.ResourceTypes.ToList();
                var typeIds = await _context.ResourceTypes
                    .Where(rt => resourceTypeNames.Contains(rt.Name))
                    .Select(rt => rt.ResourceTypeId)
                    .ToListAsync(ct);

                if (typeIds.Count == 0)
                {
                    // No valid types found, return 0
                    return 0;
                }

                multiTypeBaseQuery = _context.Resources
                    .Where(r => typeIds.Contains(r.ResourceTypeId)
                        && !r.IsHistory
                        && !r.IsDeleted);

                _logger.LogDebug("Applied _type filter for count: {Types} -> TypeIds: {TypeIds}",
                    string.Join(",", resourceTypeNames), string.Join(",", typeIds));
            }
            else
            {
                // Base query without resource type filter
                multiTypeBaseQuery = _context.Resources
                    .Where(r => !r.IsHistory && !r.IsDeleted);
            }

            // Apply search expression filters
            if (options.Expression != null)
            {
                // Pass null for resourceTypeId to indicate system-wide search across all types
                var filteredQuery = await _queryBuilder.ApplySearchExpressionAsync(
                    multiTypeBaseQuery,
                    null, // null means system-wide search - query all resource types
                    options.Expression,
                    ct);
                return await filteredQuery.CountAsync(ct);
            }

            return await multiTypeBaseQuery.CountAsync(ct);
        }

        // Get ResourceTypeId for single-type searches
        var resourceTypeId = await GetResourceTypeIdAsync(options.ResourceType, ct);
        if (!resourceTypeId.HasValue)
        {
            return 0;
        }

        // Start with base query
        var baseQuery = _context.Resources
            .Where(r => r.ResourceTypeId == resourceTypeId.Value
                && !r.IsHistory
                && !r.IsDeleted);

        // Apply search expression filters
        if (options.Expression != null)
        {
            var filteredQuery = await _queryBuilder.ApplySearchExpressionAsync(
                baseQuery,
                resourceTypeId.Value,
                options.Expression,
                ct);
            return await filteredQuery.CountAsync(ct);
        }

        return await baseQuery.CountAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<(long StartId, long EndId)>> GetExportRangesAsync(
        string resourceType,
        int numberOfRanges,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Getting export ranges: ResourceType={ResourceType}, NumberOfRanges={NumberOfRanges}",
            resourceType,
            numberOfRanges);

        // Get ResourceTypeId
        var resourceTypeId = await GetResourceTypeIdAsync(resourceType, ct);
        if (!resourceTypeId.HasValue)
        {
            _logger.LogWarning("ResourceType not found: {ResourceType}", resourceType);
            return Array.Empty<(long, long)>();
        }

        // Query for min/max/count in a single aggregation query (optimized to avoid 3 separate subqueries)
        var stats = await _context.Resources
            .Where(r => r.ResourceTypeId == resourceTypeId.Value
                && !r.IsHistory
                && !r.IsDeleted)
            .AsNoTracking()
            .GroupBy(r => 1)
            .Select(g => new
            {
                MinId = g.Min(x => x.ResourceSurrogateId),
                MaxId = g.Max(x => x.ResourceSurrogateId),
                Count = g.Count()
            })
            .FirstOrDefaultAsync(ct);

        if (stats == null || stats.Count == 0)
        {
            _logger.LogInformation("No resources found for ResourceType={ResourceType}", resourceType);
            return Array.Empty<(long, long)>();
        }

        long minId = stats.MinId;
        long maxId = stats.MaxId;
        long count = stats.Count;
        long rangeSize = (long)Math.Ceiling((double)(maxId - minId + 1) / numberOfRanges);

        _logger.LogInformation(
            "Calculated export ranges: Min={MinId}, Max={MaxId}, Count={Count}, RangeSize={RangeSize}",
            minId,
            maxId,
            count,
            rangeSize);

        // Generate non-overlapping, exhaustive ranges
        var ranges = new List<(long, long)>();
        long currentStart = minId;

        for (int i = 0; i < numberOfRanges; i++)
        {
            long currentEnd = (i == numberOfRanges - 1)
                ? maxId  // Last range includes all remaining IDs
                : currentStart + rangeSize - 1;

            // Only add range if there's data in it
            if (currentStart <= maxId)
            {
                ranges.Add((currentStart, Math.Min(currentEnd, maxId)));
                _logger.LogDebug("Range {Index}: [{StartId}..{EndId}]", i + 1, currentStart, Math.Min(currentEnd, maxId));
                currentStart = currentEnd + 1;
            }
        }

        _logger.LogInformation(
            "Generated {RangeCount} export ranges for ResourceType={ResourceType}",
            ranges.Count,
            resourceType);

        return ranges.AsReadOnly();
    }

    /// <summary>
    /// Maps a ResourceEntity to a SearchEntryResult by decompressing the raw bytes.
    /// Eliminates N+1 query problem by using ResourceEntity data directly instead of repository fetch.
    /// Main search results are marked as Match in SearchMode.
    /// </summary>
    private SearchEntryResult MapResourceEntityToSearchResult(
        ResourceEntity entity,
        string resourceType)
    {
        return new SearchEntryResult(
            ResourceType: resourceType,
            ResourceId: entity.ResourceId,
            VersionId: entity.Version.ToString(),
            LastModified: entity.Transaction?.CreateDate ?? DateTimeOffset.UtcNow,
            ResourceBytes: _compressor.DecompressBytes(entity.RawResource))
        {
            IsDeleted = entity.IsDeleted,
            SearchMode = SearchEntryMode.Match,  // Main search results are matches
        };
    }

    /// <summary>
    /// Processes _include, _revinclude, and :iterate expressions and returns all discovered resources.
    /// Called after main search results are streamed to avoid blocking.
    /// </summary>
    private async Task<List<SearchEntryResult>> ProcessIncludesAndRevIncludesAsync(
        List<SearchEntryResult> mainResults,
        SearchOptions options,
        CancellationToken ct)
    {
        var allIncluded = new List<SearchEntryResult>();

        // Convert results to resource identities (ResourceType + ResourceId) for lightweight passing to processors
        var resourceIdentities = mainResults
            .Select(r => (r.ResourceType, r.ResourceId))
            .ToList();

        // Process _include expressions (non-iterate only)
        var nonIterateIncludes = options.Include.Where(e => !e.Iterate).ToList();
        if (nonIterateIncludes.Count > 0)
        {
            _logger.LogDebug("Processing {IncludeCount} _include expressions", nonIterateIncludes.Count);
            var included = await _includeProcessor.ProcessIncludesAsync(
                resourceIdentities,
                nonIterateIncludes,
                ct);

            allIncluded.AddRange(included);
            _logger.LogDebug("Added {IncludedCount} included resources", included.Count);
        }

        // Process _revinclude expressions (non-iterate only)
        var nonIterateRevIncludes = options.RevInclude.Where(e => !e.Iterate).ToList();
        if (nonIterateRevIncludes.Count > 0)
        {
            _logger.LogDebug("Processing {RevIncludeCount} non-iterate _revinclude expressions", nonIterateRevIncludes.Count);
            var revIncluded = await _revIncludeProcessor.ProcessRevIncludesAsync(
                resourceIdentities,
                nonIterateRevIncludes,
                ct);

            allIncluded.AddRange(revIncluded);
            _logger.LogDebug("Added {RevIncludedCount} reverse included resources", revIncluded.Count);
        }

        // Process :iterate modifiers (recursive includes/revincludes)
        var allIterateExpressions = options.Include
            .Concat(options.RevInclude)
            .Where(e => e.Iterate)
            .ToList();

        if (allIterateExpressions.Count > 0)
        {
            _logger.LogDebug("Processing {IterateCount} :iterate expressions", allIterateExpressions.Count);

            // Combine main results and first-level includes as starting point for iteration
            var iterationStartingPoint = mainResults.Concat(allIncluded).ToList();

            var iteratedResources = await _iterateProcessor.ProcessIteratesAsync(
                iterationStartingPoint,
                allIterateExpressions,
                ct);

            allIncluded.AddRange(iteratedResources);
            _logger.LogDebug("Added {IteratedCount} iterated resources", iteratedResources.Count);
        }

        return allIncluded;
    }

    private async Task<IQueryable<ResourceEntity>> BuildQueryAsync(
        SearchOptions options,
        short? resourceTypeId,
        CancellationToken ct,
        bool includePagination = true,
        bool forIncludeProcessing = false)
    {
        _logger.LogDebug(
            "BuildQueryAsync: ResourceType={ResourceType}, ResourceTypeId={ResourceTypeId}, " +
            "HasExpression={HasExpression}, ExpressionType={ExpressionType}",
            options.ResourceType,
            resourceTypeId,
            options.Expression != null,
            options.Expression?.GetType().Name);

            // Start with base query for current (non-history, non-deleted) resources
            IQueryable<ResourceEntity> baseQuery;

            if (resourceTypeId.HasValue)
            {
                // Single-type search: filter by specific resource type
                baseQuery = _context.Resources
                    .Where(r => r.ResourceTypeId == resourceTypeId.Value
                        && !r.IsHistory
                        && !r.IsDeleted);
            }
            else if (options.ResourceTypes.Count > 0)
            {
                // Multi-type search with _type filter: filter by specified resource types
                // Get ResourceTypeIds for the specified types
                var resourceTypeNames = options.ResourceTypes.ToList();
                var typeIds = await _context.ResourceTypes
                    .Where(rt => resourceTypeNames.Contains(rt.Name))
                    .Select(rt => rt.ResourceTypeId)
                    .ToListAsync(ct);

                if (typeIds.Count == 0)
                {
                    // No valid types found, return empty query
                    baseQuery = _context.Resources.Where(r => false);
                }
                else
                {
                    baseQuery = _context.Resources
                        .Where(r => typeIds.Contains(r.ResourceTypeId)
                            && !r.IsHistory
                            && !r.IsDeleted);
                }

                _logger.LogDebug("Applied _type filter: {Types} -> TypeIds: {TypeIds}",
                    string.Join(",", resourceTypeNames), string.Join(",", typeIds));
            }
            else
            {
                // Multi-type search: no resource type filter
                // Resource type filtering will be handled by the expression tree
                baseQuery = _context.Resources
                    .Where(r => !r.IsHistory
                        && !r.IsDeleted);
            }

            // Apply search expression filters
            IQueryable<ResourceEntity> filteredQuery;
            if (options.Expression != null)
            {
                _logger.LogDebug("Applying search expression filters");

                // For multi-type searches, pass null to skip resource type filtering in search parameter queries
                // The base query already filters by resource types via _type parameter or no filter at all
                filteredQuery = await _queryBuilder.ApplySearchExpressionAsync(
                    baseQuery,
                    resourceTypeId, // null for multi-type searches
                    options.Expression,
                    ct);
            }
            else
            {
                filteredQuery = baseQuery;
            }

            // Apply surrogate ID range filtering for export partitioning
            if (options.StartSurrogateId.HasValue && options.EndSurrogateId.HasValue)
            {
                filteredQuery = filteredQuery.Where(r =>
                    r.ResourceSurrogateId >= options.StartSurrogateId.Value &&
                    r.ResourceSurrogateId <= options.EndSurrogateId.Value);

                _logger.LogDebug(
                    "Applied surrogate ID range filter: [{StartId}..{EndId}]",
                    options.StartSurrogateId.Value,
                    options.EndSurrogateId.Value);
            }

            // Apply sorting
            _logger.LogDebug("Applying sorting");
            IOrderedQueryable<ResourceEntity> sortedQuery = ApplySorting(filteredQuery, options.Sort);

            // Skip pagination when explicitly requested (e.g., for count queries or internal processing)
            if (!includePagination)
            {
                return sortedQuery;
            }

            // Apply pagination: parse continuation token or use _count parameter
            int offset = 0;
            int pageSize = options.MaxItemCount;

            if (!string.IsNullOrWhiteSpace(options.ContinuationToken))
            {
                // Decode continuation token to get offset
                if (Ignixa.Search.Models.ContinuationToken.TryDecode(
                    options.ContinuationToken, out int tokenOffset, out int tokenCount))
                {
                    offset = tokenOffset;
                    // Token stores original user-requested count (without +1),
                    // but handler adds +1 for hasMore detection - so we add it back here
                    pageSize = tokenCount + 1;
                    _logger.LogDebug("Using continuation token: offset={Offset}, count={Count} (+1 for hasMore)", offset, pageSize);
                }
                else
                {
                    _logger.LogWarning("Invalid continuation token, using offset=0");
                }
            }

            // Apply Skip/Take for pagination
            // NOTE: The handler layer (SearchResourcesHandler) already adds +1 for hasMore detection,
            // so we just use pageSize as-is for main queries.
            // For include processing, we need to subtract 1 since we only want the actual page results
            // (not the +1 extra for hasMore detection).
            int takeCount = forIncludeProcessing ? Math.Max(1, pageSize - 1) : pageSize;
            return sortedQuery.Skip(offset).Take(takeCount);
    }

    private IOrderedQueryable<ResourceEntity> ApplySorting(
        IQueryable<ResourceEntity> query,
        IReadOnlyList<SortExpression>? sortOptions)
    {
        if (sortOptions == null || sortOptions.Count == 0)
        {
            // Default sort: by ResourceSurrogateId ascending (oldest first)
            _logger.LogDebug("No sort options provided, using default sort by ResourceSurrogateId");
            return query.OrderBy(r => r.ResourceSurrogateId);
        }

        _logger.LogDebug("Applying {Count} sort expression(s): {Sorts}",
            sortOptions.Count,
            string.Join(", ", sortOptions.Select(s => $"{s.Parameter.Code} ({s.SortOrder})")));

        // Apply primary sort
        var firstSort = sortOptions[0];
        IOrderedQueryable<ResourceEntity> orderedQuery = ApplySort(query, firstSort, isPrimary: true);

        // Apply secondary sorts (ThenBy/ThenByDescending)
        for (int i = 1; i < sortOptions.Count; i++)
        {
            orderedQuery = ApplyThenBy(orderedQuery, sortOptions[i]);
        }

        // CRITICAL: Always add final sort by ResourceSurrogateId for deterministic ordering
        // This ensures:
        // 1. Resources with NULL values for the sort parameter have consistent positioning
        // 2. Pagination with continuation tokens is stable (same query = same order)
        // 3. Multiple resources with identical sort values are ordered predictably
        orderedQuery = orderedQuery.ThenBy(r => r.ResourceSurrogateId);

        return orderedQuery;
    }

    private IOrderedQueryable<ResourceEntity> ApplySort(
        IQueryable<ResourceEntity> query,
        SortExpression sortExpr,
        bool isPrimary)
    {
        bool isDescending = sortExpr.SortOrder == SortOrder.Descending;

        // Handle common sorts: _id, _lastUpdated
        if (sortExpr.Parameter.Code == "_id")
        {
            return isDescending
                ? query.OrderByDescending(r => r.ResourceId)
                : query.OrderBy(r => r.ResourceId);
        }

        if (sortExpr.Parameter.Code == "_lastUpdated")
        {
            return isDescending
                ? query.OrderByDescending(r => r.Transaction!.CreateDate)
                : query.OrderBy(r => r.Transaction!.CreateDate);
        }

        // Search parameter-based sorting
        // Use LEFT JOIN with subqueries to handle resources without the parameter (nulls sort last)

        // Pre-compute SearchParamId with OverridesUrl fallback support
        // IMPORTANT: Must be done before entering LINQ expression (LINQ to SQL requires constants)
#pragma warning disable CA2012 // Use ValueTasks correctly - Must block synchronously in EF LINQ expression context
        var searchParamId = _cache.GetSearchParamIdAsync(sortExpr.Parameter).GetAwaiter().GetResult();
#pragma warning restore CA2012
        if (!searchParamId.HasValue)
        {
            _logger.LogWarning(
                "Search parameter not found for _sort: {Url}. Falling back to default sort.",
                sortExpr.Parameter.Url);
            return isDescending
                ? query.OrderByDescending(r => r.ResourceId)
                : query.OrderBy(r => r.ResourceId);
        }

        var paramType = sortExpr.Parameter.Type;

        switch (paramType)
        {
            case SearchParamType.String:
                // LEFT JOIN with StringSearchParam, use MIN/MAX aggregation for multi-value parameters
                // NOTE: SQL Server default behavior - NULLs sort FIRST in ascending, LAST in descending
                // Resources without this parameter will appear at beginning (ASC) or end (DESC)
                // Final ThenBy(ResourceSurrogateId) ensures deterministic ordering for NULL values
                return isDescending
                    ? query.OrderByDescending(r =>
                        _context.StringSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == searchParamId.Value)
                            .Select(sp => sp.Text)
                            .Max())
                    : query.OrderBy(r =>
                        _context.StringSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == searchParamId.Value)
                            .Select(sp => sp.Text)
                            .Min());

            case SearchParamType.Date:
                // LEFT JOIN with DateTimeSearchParam
                return isDescending
                    ? query.OrderByDescending(r =>
                        _context.DateTimeSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == searchParamId.Value)
                            .Select(sp => sp.StartDateTime)
                            .Max())
                    : query.OrderBy(r =>
                        _context.DateTimeSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == searchParamId.Value)
                            .Select(sp => sp.StartDateTime)
                            .Min());

            case SearchParamType.Token:
                // LEFT JOIN with TokenSearchParam, sort by Code
                return isDescending
                    ? query.OrderByDescending(r =>
                        _context.TokenSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == searchParamId.Value)
                            .Select(sp => sp.Code)
                            .Max())
                    : query.OrderBy(r =>
                        _context.TokenSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == searchParamId.Value)
                            .Select(sp => sp.Code)
                            .Min());

            case SearchParamType.Number:
                // LEFT JOIN with NumberSearchParam
                return isDescending
                    ? query.OrderByDescending(r =>
                        _context.NumberSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == searchParamId.Value)
                            .Select(sp => sp.LowValue)
                            .Max())
                    : query.OrderBy(r =>
                        _context.NumberSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == searchParamId.Value)
                            .Select(sp => sp.LowValue)
                            .Min());

            case SearchParamType.Quantity:
                // LEFT JOIN with QuantitySearchParam
                return isDescending
                    ? query.OrderByDescending(r =>
                        _context.QuantitySearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == searchParamId.Value)
                            .Select(sp => sp.LowValue)
                            .Max())
                    : query.OrderBy(r =>
                        _context.QuantitySearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == searchParamId.Value)
                            .Select(sp => sp.LowValue)
                            .Min());

            case SearchParamType.Reference:
                // LEFT JOIN with ReferenceSearchParam, sort by ReferenceResourceId
                return isDescending
                    ? query.OrderByDescending(r =>
                        _context.ReferenceSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == searchParamId.Value)
                            .Select(sp => sp.ReferenceResourceId)
                            .Max())
                    : query.OrderBy(r =>
                        _context.ReferenceSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == searchParamId.Value)
                            .Select(sp => sp.ReferenceResourceId)
                            .Min());

            case SearchParamType.Uri:
                // LEFT JOIN with UriSearchParam
                return isDescending
                    ? query.OrderByDescending(r =>
                        _context.UriSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == searchParamId.Value)
                            .Select(sp => sp.Uri)
                            .Max())
                    : query.OrderBy(r =>
                        _context.UriSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == searchParamId.Value)
                            .Select(sp => sp.Uri)
                            .Min());

            default:
                // Unsupported parameter type: silently fall back to default sort (lenient error handling per FHIR spec)
                _logger.LogWarning(
                    "Sorting not supported for parameter type {Type} ({Parameter}), using default sort",
                    paramType,
                    sortExpr.Parameter.Code);
                return query.OrderByDescending(r => r.ResourceSurrogateId);
        }
    }

    /// <summary>
    /// Helper method to get search parameter ID from URL using in-memory cache only.
    /// Uses synchronous cache lookup - returns 0 if not found in cache (lenient fallback).
    /// Parameter IDs must be pre-cached during startup by calling PreloadSearchParamsAsync().
    /// This method is called from within LINQ expressions, so it must be synchronous and fast.
    /// </summary>
    private short GetSearchParamIdFromUrl(Uri url)
    {
        if (url == null)
        {
            _logger.LogWarning("SearchParam URL is null, sort will have no effect");
            return 0;
        }

        // Convert Uri to string and use cache-only lookup
        string urlString = url.ToString();
        short searchParamId = _cache.TryGetSearchParamIdFromCache(urlString);

        if (searchParamId == 0)
        {
            _logger.LogWarning("SearchParam not found in cache for URL: {Url}, sort will have no effect", urlString);
        }

        return searchParamId;
    }

    /// <summary>
    /// Gets the SearchParamId for a search parameter, with support for OverridesUrl fallback.
    /// This is the preferred method for include/revinclude queries where Implementation Guide
    /// parameters (e.g., US Core) may override base FHIR parameters.
    /// Uses the cache's async method but blocks synchronously since EF queries require sync execution.
    /// </summary>
    /// <param name="searchParameter">The search parameter containing URL and optional OverridesUrl.</param>
    /// <returns>The SearchParamId, or 0 if not found.</returns>
#pragma warning disable CA2012 // Use ValueTasks correctly - Must block synchronously in EF LINQ expression context
    private short GetSearchParamIdFromSearchParameter(SearchParameterInfo searchParameter)
    {
        if (searchParameter?.Url == null)
        {
            _logger.LogWarning("SearchParameter or URL is null");
            return 0;
        }

        // Use the cache's async method that handles OverridesUrl fallback, but block synchronously
        // This is acceptable since BuildIncludeQuery/BuildRevIncludeQuery are already using .GetAwaiter().GetResult()
        var searchParamId = _cache.GetSearchParamIdAsync(searchParameter).AsTask().GetAwaiter().GetResult();

        if (!searchParamId.HasValue)
        {
            _logger.LogWarning(
                "Search parameter not found for URL: {Url} (OverridesUrl: {OverridesUrl})",
                searchParameter.Url,
                searchParameter.OverridesUrl);
            return 0;
        }

        return searchParamId.Value;
    }
#pragma warning restore CA2012

    private IOrderedQueryable<ResourceEntity> ApplyThenBy(
        IOrderedQueryable<ResourceEntity> query,
        SortExpression sortExpr)
    {
        bool isDescending = sortExpr.SortOrder == SortOrder.Descending;

        // Handle common sorts: _id, _lastUpdated
        if (sortExpr.Parameter.Code == "_id")
        {
            return isDescending
                ? query.ThenByDescending(r => r.ResourceId)
                : query.ThenBy(r => r.ResourceId);
        }

        if (sortExpr.Parameter.Code == "_lastUpdated")
        {
            // Sort by Transaction.CreateDate
            return isDescending
                ? query.ThenByDescending(r => r.Transaction!.CreateDate)
                : query.ThenBy(r => r.Transaction!.CreateDate);
        }

        // Search parameter-based secondary sorting
        // Use LEFT JOIN with subqueries, similar to primary sort

        // Pre-compute SearchParamId with OverridesUrl fallback support
        // IMPORTANT: Must be done before entering LINQ expression (LINQ to SQL requires constants)
#pragma warning disable CA2012 // Use ValueTasks correctly - Must block synchronously in EF LINQ expression context
        var searchParamId = _cache.GetSearchParamIdAsync(sortExpr.Parameter).GetAwaiter().GetResult();
#pragma warning restore CA2012
        if (!searchParamId.HasValue)
        {
            _logger.LogWarning(
                "Search parameter not found for _sort (ThenBy): {Url}. Falling back to default sort.",
                sortExpr.Parameter.Url);
            return isDescending
                ? query.ThenByDescending(r => r.ResourceId)
                : query.ThenBy(r => r.ResourceId);
        }

        var paramType = sortExpr.Parameter.Type;

        switch (paramType)
        {
            case SearchParamType.String:
                // LEFT JOIN with StringSearchParam, use MIN/MAX aggregation for multi-value parameters
                return isDescending
                    ? query.ThenByDescending(r =>
                        _context.StringSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == searchParamId.Value)
                            .Select(sp => sp.Text)
                            .Max())
                    : query.ThenBy(r =>
                        _context.StringSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == searchParamId.Value)
                            .Select(sp => sp.Text)
                            .Min());

            case SearchParamType.Date:
                // LEFT JOIN with DateTimeSearchParam
                return isDescending
                    ? query.ThenByDescending(r =>
                        _context.DateTimeSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == searchParamId.Value)
                            .Select(sp => sp.StartDateTime)
                            .Max())
                    : query.ThenBy(r =>
                        _context.DateTimeSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == searchParamId.Value)
                            .Select(sp => sp.StartDateTime)
                            .Min());

            case SearchParamType.Token:
                // LEFT JOIN with TokenSearchParam, sort by Code
                return isDescending
                    ? query.ThenByDescending(r =>
                        _context.TokenSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == searchParamId.Value)
                            .Select(sp => sp.Code)
                            .Max())
                    : query.ThenBy(r =>
                        _context.TokenSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == searchParamId.Value)
                            .Select(sp => sp.Code)
                            .Min());

            case SearchParamType.Number:
                // LEFT JOIN with NumberSearchParam
                return isDescending
                    ? query.ThenByDescending(r =>
                        _context.NumberSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == searchParamId.Value)
                            .Select(sp => sp.LowValue)
                            .Max())
                    : query.ThenBy(r =>
                        _context.NumberSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == searchParamId.Value)
                            .Select(sp => sp.LowValue)
                            .Min());

            case SearchParamType.Quantity:
                // LEFT JOIN with QuantitySearchParam
                return isDescending
                    ? query.ThenByDescending(r =>
                        _context.QuantitySearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == searchParamId.Value)
                            .Select(sp => sp.LowValue)
                            .Max())
                    : query.ThenBy(r =>
                        _context.QuantitySearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == searchParamId.Value)
                            .Select(sp => sp.LowValue)
                            .Min());

            case SearchParamType.Reference:
                // LEFT JOIN with ReferenceSearchParam, sort by ReferenceResourceId
                return isDescending
                    ? query.ThenByDescending(r =>
                        _context.ReferenceSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == searchParamId.Value)
                            .Select(sp => sp.ReferenceResourceId)
                            .Max())
                    : query.ThenBy(r =>
                        _context.ReferenceSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == searchParamId.Value)
                            .Select(sp => sp.ReferenceResourceId)
                            .Min());

            case SearchParamType.Uri:
                // LEFT JOIN with UriSearchParam
                return isDescending
                    ? query.ThenByDescending(r =>
                        _context.UriSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == searchParamId.Value)
                            .Select(sp => sp.Uri)
                            .Max())
                    : query.ThenBy(r =>
                        _context.UriSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == searchParamId.Value)
                            .Select(sp => sp.Uri)
                            .Min());

            default:
                // Unsupported parameter type: silently return unchanged query (lenient behavior)
                _logger.LogWarning(
                    "Secondary sort not supported for parameter type {Type} ({Parameter}), skipping",
                    paramType,
                    sortExpr.Parameter.Code);
                return query;
        }
    }

    /// <summary>
    /// Builds a CTE-based include query that replicates the main query filters without buffering IDs.
    /// Uses SQL Common Table Expression (CTE) pattern to find resources referenced by main results.
    /// Implements DISTINCT to deduplicate resources referenced by multiple main results.
    /// Supports both specific search parameter includes and wildcard includes (_include=Type:*).
    /// </summary>
    private IQueryable<ResourceEntity> BuildIncludeQuery(
        SearchOptions options,
        IncludeExpression includeExpr,
        short resourceTypeId)
    {
        // Step 1: Build main query as CTE (replicate filters, sort, AND pagination)
        // Include should only include resources referenced by the CURRENT PAGE of results per FHIR spec
        // Use forIncludeProcessing=true to avoid the +1 hasMore detection (only need exact pageSize)
        IQueryable<ResourceEntity> baseQuery = BuildQueryAsync(options, resourceTypeId, CancellationToken.None, includePagination: true, forIncludeProcessing: true).GetAwaiter().GetResult();

        // Extract surrogate IDs from the base query BEFORE using in .Contains()
        // This prevents EF Core from generating complex subqueries with sorting in the WHERE clause
        var mainResultSurrogateIds = baseQuery
            .Select(r => r.ResourceSurrogateId)
            .Distinct();

        if (includeExpr.WildCard)
        {
            // Wildcard include: fetch ALL referenced resources from ALL reference search parameters
            _logger.LogDebug("Building wildcard include query for source type {SourceType}", includeExpr.SourceResourceType);

            var referencedTypeAndIds = _context.ReferenceSearchParams
                .Where(rsp => mainResultSurrogateIds.Contains(rsp.ResourceSurrogateId)
                    && rsp.ReferenceResourceTypeId != null)
                .Select(rsp => new { rsp.ReferenceResourceTypeId, rsp.ReferenceResourceId })
                .Distinct();

            // Get actual resources for these references using subquery join
            // CRITICAL: Exclude resources that are already in the main result set
            // When a resource references itself (e.g., Location.partOf = self), it should appear
            // only once with search.mode="match", not twice (once as match, once as include)
            // FHIR spec: "match" mode takes precedence over "include" mode
            return _context.Resources
                .Where(r => !r.IsHistory && !r.IsDeleted &&
                            referencedTypeAndIds.Any(x => x.ReferenceResourceTypeId == r.ResourceTypeId && x.ReferenceResourceId == r.ResourceId) &&
                            !mainResultSurrogateIds.Contains(r.ResourceSurrogateId))
                .Include(r => r.Transaction)
                .Include(r => r.ResourceType)
                .Distinct()
                .OrderBy(r => r.ResourceSurrogateId);
        }
        else
        {
            // Specific search parameter include
            // Get SearchParamId from the reference search parameter (handles OverridesUrl for IG parameters like US Core)
            short searchParamId = GetSearchParamIdFromSearchParameter(includeExpr.ReferenceSearchParameter);
            if (searchParamId == 0)
            {
                _logger.LogWarning("Search parameter not found for include: {Parameter}", includeExpr.ReferenceSearchParameter?.Code);
                return _context.Resources.Where(r => false);  // Return empty
            }

            // Find resources referenced by these main results using a subquery
            // This keeps everything in the database query (no client-side materialization)
            var referencedTypeAndIds = _context.ReferenceSearchParams
                .Where(rsp => mainResultSurrogateIds.Contains(rsp.ResourceSurrogateId)
                    && rsp.SearchParamId == searchParamId
                    && rsp.ReferenceResourceTypeId != null)
                .Select(rsp => new { rsp.ReferenceResourceTypeId, rsp.ReferenceResourceId })
                .Distinct();

            // Get actual resources for these references using subquery join
            // CRITICAL: Exclude resources that are already in the main result set
            // When a resource references itself (e.g., Location.partOf = self), it should appear
            // only once with search.mode="match", not twice (once as match, once as include)
            // FHIR spec: "match" mode takes precedence over "include" mode
            return _context.Resources
                .Where(r => !r.IsHistory && !r.IsDeleted &&
                            referencedTypeAndIds.Any(x => x.ReferenceResourceTypeId == r.ResourceTypeId && x.ReferenceResourceId == r.ResourceId) &&
                            !mainResultSurrogateIds.Contains(r.ResourceSurrogateId))
                .Include(r => r.Transaction)
                .Include(r => r.ResourceType)
                .Distinct()
                .OrderBy(r => r.ResourceSurrogateId);
        }
    }

    /// <summary>
    /// Builds a CTE-based revinclude query that finds resources referencing the main results.
    /// Uses SQL Common Table Expression (CTE) pattern.
    /// Implements DISTINCT to deduplicate resources that reference multiple main results.
    /// Supports three patterns:
    /// - Specific: _revinclude=Type:param (specific source type and search parameter)
    /// - Wildcard param: _revinclude=Type:* (specific source type, any search parameter)
    /// - Wildcard source: _revinclude=*:* (any source type, any search parameter)
    /// </summary>
    private IQueryable<ResourceEntity> BuildRevIncludeQuery(
        SearchOptions options,
        IncludeExpression revIncludeExpr,
        short resourceTypeId)
    {
        // Build main query using BuildQueryAsync (filters, sorting, AND pagination)
        // Revinclude should only include resources that reference the CURRENT PAGE of results per FHIR spec
        // Use forIncludeProcessing=true to avoid the +1 hasMore detection (only need exact pageSize)
        IQueryable<ResourceEntity> baseQuery = BuildQueryAsync(options, resourceTypeId, CancellationToken.None, includePagination: true, forIncludeProcessing: true)
            .GetAwaiter().GetResult();

        // Extract resource type and ID pairs from the base query BEFORE using in .Any()
        // This prevents EF Core from generating complex subqueries with sorting in EXISTS clauses
        // which can cause translation errors or performance issues
        var mainResultIdentifiers = baseQuery
            .Select(r => new { r.ResourceTypeId, r.ResourceId })
            .Distinct();

        // Also extract surrogate IDs for deduplication (exclude main results from revinclude)
        var mainResultSurrogateIds = baseQuery
            .Select(r => r.ResourceSurrogateId)
            .Distinct();

        IQueryable<long> referencingRsps;

        // Check for wildcard source type (*:*) - find ALL resources of ANY type that reference main results
        bool isWildcardSource = revIncludeExpr.SourceResourceType == "*";

        if (isWildcardSource)
        {
            // Wildcard source revinclude (_revinclude=*:*): find ALL resources of ANY type that reference main results
            // This is the most inclusive pattern - no filtering by source resource type or search parameter
            _logger.LogDebug("Building wildcard source revinclude query (*:*) for main results");

            referencingRsps = _context.ReferenceSearchParams
                .Where(rsp => mainResultIdentifiers.Any(mr => mr.ResourceTypeId == rsp.ReferenceResourceTypeId && mr.ResourceId == rsp.ReferenceResourceId))
                .Select(rsp => rsp.ResourceSurrogateId)
                .Distinct();
        }
        else
        {
            // Get source resource type ID (e.g., Encounter for _revinclude=Encounter:subject)
            short? sourceResourceTypeId = GetResourceTypeIdAsync(revIncludeExpr.SourceResourceType, CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();
            if (!sourceResourceTypeId.HasValue)
            {
                _logger.LogWarning("Source resource type not found for revinclude: {SourceType}", revIncludeExpr.SourceResourceType);
                return _context.Resources.Where(r => false);  // Return empty
            }

            if (revIncludeExpr.WildCard)
            {
                // Wildcard param revinclude (_revinclude=Type:*): find ALL resources of the source type that reference main results
                // using ANY reference search parameter
                _logger.LogDebug("Building wildcard param revinclude query for source type {SourceType}", revIncludeExpr.SourceResourceType);

                referencingRsps = _context.ReferenceSearchParams
                    .Where(rsp => rsp.ResourceTypeId == sourceResourceTypeId.Value &&
                                  mainResultIdentifiers.Any(mr => mr.ResourceTypeId == rsp.ReferenceResourceTypeId && mr.ResourceId == rsp.ReferenceResourceId))
                    .Select(rsp => rsp.ResourceSurrogateId)
                    .Distinct();
            }
            else
            {
                // Specific search parameter revinclude
                // Get SearchParamId from the reference search parameter (handles OverridesUrl for IG parameters like US Core)
                short searchParamId = GetSearchParamIdFromSearchParameter(revIncludeExpr.ReferenceSearchParameter);
                if (searchParamId == 0)
                {
                    _logger.LogWarning("Search parameter not found for revinclude: {Parameter}", revIncludeExpr.ReferenceSearchParameter?.Code);
                    return _context.Resources.Where(r => false);  // Return empty
                }

                // Find resources that reference these main results using a subquery
                // Filter by source resource type (e.g., Encounter), search parameter, and reference target
                // This keeps everything in the database query (no client-side materialization)
                referencingRsps = _context.ReferenceSearchParams
                    .Where(rsp => rsp.ResourceTypeId == sourceResourceTypeId.Value &&
                                  rsp.SearchParamId == searchParamId &&
                                  mainResultIdentifiers.Any(mr => mr.ResourceTypeId == rsp.ReferenceResourceTypeId && mr.ResourceId == rsp.ReferenceResourceId))
                    .Select(rsp => rsp.ResourceSurrogateId)
                    .Distinct();
            }
        }

        // Get the actual resources using subquery containment
        // CRITICAL: Exclude resources that are already in the main result set
        // When a resource references itself (e.g., Location.partOf = self), it should appear
        // only once with search.mode="match", not twice (once as match, once as include)
        // FHIR spec: "match" mode takes precedence over "include" mode
        return _context.Resources
            .Where(r => referencingRsps.Contains(r.ResourceSurrogateId) &&
                        !mainResultSurrogateIds.Contains(r.ResourceSurrogateId) &&
                        !r.IsHistory && !r.IsDeleted)
            .Include(r => r.Transaction)
            .Include(r => r.ResourceType)
            .Distinct()
            .OrderBy(r => r.ResourceSurrogateId);  // Stable order for streaming
    }

    private async ValueTask<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken ct)
    {
        var entity = await _context.ResourceTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(rt => rt.Name == resourceType, ct);

        return entity?.ResourceTypeId;
    }

    /// <summary>
    /// Gets the SearchParamId for a given search parameter URL.
    /// This is a synchronous wrapper around the async cache lookup.
    /// IMPORTANT: This method is called inside EF Core LINQ expressions, which means we CANNOT use async/await
    /// (LINQ to SQL requires synchronous expressions). We use .GetAwaiter().GetResult() to block synchronously.
    /// The cache has an in-memory dictionary, so this is typically fast (only first call may hit database).
    /// </summary>
    /// <param name="url">The search parameter URL (e.g., "http://hl7.org/fhir/SearchParameter/Patient-birthdate").</param>
    /// <returns>The SearchParamId (short).</returns>
    /// <exception cref="InvalidOperationException">Thrown if the search parameter is not found in the database.</exception>
#pragma warning disable CA2012 // Use ValueTasks correctly - Must block synchronously in EF LINQ expression context
    private short GetSearchParamIdFromUrl(string url)
    {
        var result = _cache.GetSearchParamIdAsync(url).GetAwaiter().GetResult();
#pragma warning restore CA2012

        if (result == null)
        {
            _logger.LogError("Search parameter not found for URL: {Url}. Ensure search parameters are loaded in database.", url);
            throw new InvalidOperationException($"Search parameter not found for URL: {url}");
        }

        return result.Value;
    }
}
