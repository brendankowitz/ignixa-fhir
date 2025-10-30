// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ignixa.DataLayer.SqlEntityFramework.Compression;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
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
    /// <param name="logger">Logger instance.</param>
    public SqlEntityFrameworkSearchService(
        FhirDbContext context,
        IFhirRepository repository,
        SearchExpressionQueryBuilder queryBuilder,
        IncludeProcessor includeProcessor,
        RevIncludeProcessor revIncludeProcessor,
        IterateProcessor iterateProcessor,
        GzipResourceCompressor compressor,
        ILogger<SqlEntityFrameworkSearchService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _queryBuilder = queryBuilder ?? throw new ArgumentNullException(nameof(queryBuilder));
        _includeProcessor = includeProcessor ?? throw new ArgumentNullException(nameof(includeProcessor));
        _revIncludeProcessor = revIncludeProcessor ?? throw new ArgumentNullException(nameof(revIncludeProcessor));
        _iterateProcessor = iterateProcessor ?? throw new ArgumentNullException(nameof(iterateProcessor));
        _compressor = compressor ?? throw new ArgumentNullException(nameof(compressor));
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

            _logger.LogInformation("Executing streaming query for multi-type search\n{SQL}", multiTypeQuery.ToQueryString());

            // Stream results from all matching resources - buffer for include processing
            var multiTypeMainResults = new List<SearchEntryResult>();
            await foreach (var entity in multiTypeQuery
                .Include(x => x.Transaction)
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

        _logger.LogInformation("Executing streaming query for {ResourceType}\n{SQL}", options.ResourceType, query.ToQueryString());

        // Phase 1: Stream main results (NO buffering - zero memory for large result sets)
        await foreach (var entity in query
            .Include(x => x.Transaction)
            .AsAsyncEnumerable().WithCancellation(ct))
        {
            var searchResult = MapResourceEntityToSearchResult(entity, options.ResourceType);
            yield return searchResult;  // Stream immediately to client
        }

        // Phase 2: Stream included resources (if _include parameters exist)
        if (options.Include.Count > 0)
        {
            _logger.LogDebug("Processing {IncludeCount} _include expressions", options.Include.Count);

            foreach (var includeExpr in options.Include)
            {
                var includeQuery = BuildIncludeQuery(options, includeExpr, resourceTypeId.Value);

                _logger.LogDebug("Executing include query for parameter {ParamCode}", includeExpr.ReferenceSearchParameter?.Code);

                // Stream each included resource directly to client
                await foreach (var entity in includeQuery
                    .AsAsyncEnumerable().WithCancellation(ct))
                {
                    var searchResult = MapResourceEntityToSearchResult(entity, includeExpr.TargetResourceType);
                    searchResult = searchResult with { SearchMode = SearchEntryMode.Include };
                    yield return searchResult;
                }
            }

            _logger.LogDebug("Include processing completed");
        }

        // Phase 3: Stream reverse-included resources (if _revinclude parameters exist)
        if (options.RevInclude.Count > 0)
        {
            _logger.LogDebug("Processing {RevIncludeCount} _revinclude expressions", options.RevInclude.Count);

            foreach (var revIncludeExpr in options.RevInclude)
            {
                var revIncludeQuery = BuildRevIncludeQuery(options, revIncludeExpr, resourceTypeId.Value);

                _logger.LogDebug("Executing revinclude query for parameter {ParamCode} with {Sql}", revIncludeExpr.ReferenceSearchParameter?.Code, revIncludeQuery.ToQueryString());

                // Stream each reverse-included resource directly to client
                await foreach (var entity in revIncludeQuery
                    .AsAsyncEnumerable().WithCancellation(ct))
                {
                    // Determine resource type from entity
                    var resourceTypeName = revIncludeExpr.SourceResourceType ?? _context.ResourceTypes
                        .AsNoTracking()
                        .Where(rt => rt.ResourceTypeId == entity.ResourceTypeId)
                        .Select(rt => rt.Name)
                        .First();

                    var searchResult = MapResourceEntityToSearchResult(entity, resourceTypeName);
                    searchResult = searchResult with { SearchMode = SearchEntryMode.Include };
                    yield return searchResult;
                }
            }

            _logger.LogDebug("RevInclude processing completed");
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

        _logger.LogDebug("Counting resources for {ResourceType}", options.ResourceType);

        // Get ResourceTypeId
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

        // Process _include expressions
        if (options.Include.Count > 0)
        {
            _logger.LogDebug("Processing {IncludeCount} _include expressions", options.Include.Count);
            var included = await _includeProcessor.ProcessIncludesAsync(
                resourceIdentities,
                options.Include,
                ct);

            allIncluded.AddRange(included);
            _logger.LogDebug("Added {IncludedCount} included resources", included.Count);
        }

        // Process _revinclude expressions
        if (options.RevInclude.Count > 0)
        {
            _logger.LogDebug("Processing {RevIncludeCount} _revinclude expressions", options.RevInclude.Count);
            var revIncluded = await _revIncludeProcessor.ProcessRevIncludesAsync(
                resourceIdentities,
                options.RevInclude,
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
            var iteratedResources = await _iterateProcessor.ProcessIteratesAsync(
                allIncluded.Count > 0 ? allIncluded : mainResults,
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
        CancellationToken ct)
    {
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
            // For multi-type searches, we pass a dummy resourceTypeId (not used by expressions like CompartmentSearchExpression)
            var typeIdForExpression = resourceTypeId ?? 1; // Use 1 as dummy value for multi-type

            filteredQuery = await _queryBuilder.ApplySearchExpressionAsync(
                baseQuery,
                typeIdForExpression,
                options.Expression,
                ct);
        }
        else
        {
            filteredQuery = baseQuery;
        }

        // Apply sorting
        var sortedQuery = ApplySorting(filteredQuery, options.Sort);

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
                pageSize = tokenCount; // Use count from token for consistency
                _logger.LogDebug("Using continuation token: offset={Offset}, count={Count}", offset, pageSize);
            }
            else
            {
                _logger.LogWarning("Invalid continuation token, using offset=0");
            }
        }

        // Apply Skip/Take for pagination (limit results to pageSize + 1 to detect if more results exist)
        return sortedQuery.Skip(offset).Take(pageSize + 1);
    }

    private IOrderedQueryable<ResourceEntity> ApplySorting(
        IQueryable<ResourceEntity> query,
        IReadOnlyList<SortExpression>? sortOptions)
    {
        if (sortOptions == null || sortOptions.Count == 0)
        {
            // Default sort: by ResourceSurrogateId ascending (oldest first)
            return query.OrderBy(r => r.ResourceSurrogateId);
        }

        // Apply primary sort
        var firstSort = sortOptions[0];
        IOrderedQueryable<ResourceEntity> orderedQuery = ApplySort(query, firstSort, isPrimary: true);

        // Apply secondary sorts (ThenBy/ThenByDescending)
        for (int i = 1; i < sortOptions.Count; i++)
        {
            orderedQuery = ApplyThenBy(orderedQuery, sortOptions[i]);
        }

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
        var paramType = sortExpr.Parameter.Type;

        switch (paramType)
        {
            case SearchParamType.String:
                // LEFT JOIN with StringSearchParam, use MIN/MAX aggregation for multi-value parameters
                // Nulls (resources without this parameter) sort last
                return isDescending
                    ? query.OrderByDescending(r =>
                        _context.StringSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == GetSearchParamIdFromUrl(sortExpr.Parameter.Url))
                            .Select(sp => sp.Text)
                            .Max())
                    : query.OrderBy(r =>
                        _context.StringSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == GetSearchParamIdFromUrl(sortExpr.Parameter.Url))
                            .Select(sp => sp.Text)
                            .Min());

            case SearchParamType.Date:
                // LEFT JOIN with DateTimeSearchParam
                return isDescending
                    ? query.OrderByDescending(r =>
                        _context.DateTimeSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == GetSearchParamIdFromUrl(sortExpr.Parameter.Url))
                            .Select(sp => sp.StartDateTime)
                            .Max())
                    : query.OrderBy(r =>
                        _context.DateTimeSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == GetSearchParamIdFromUrl(sortExpr.Parameter.Url))
                            .Select(sp => sp.StartDateTime)
                            .Min());

            case SearchParamType.Token:
                // LEFT JOIN with TokenSearchParam, sort by Code
                return isDescending
                    ? query.OrderByDescending(r =>
                        _context.TokenSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == GetSearchParamIdFromUrl(sortExpr.Parameter.Url))
                            .Select(sp => sp.Code)
                            .Max())
                    : query.OrderBy(r =>
                        _context.TokenSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == GetSearchParamIdFromUrl(sortExpr.Parameter.Url))
                            .Select(sp => sp.Code)
                            .Min());

            case SearchParamType.Number:
                // LEFT JOIN with NumberSearchParam
                return isDescending
                    ? query.OrderByDescending(r =>
                        _context.NumberSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == GetSearchParamIdFromUrl(sortExpr.Parameter.Url))
                            .Select(sp => sp.LowValue)
                            .Max())
                    : query.OrderBy(r =>
                        _context.NumberSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == GetSearchParamIdFromUrl(sortExpr.Parameter.Url))
                            .Select(sp => sp.LowValue)
                            .Min());

            case SearchParamType.Quantity:
                // LEFT JOIN with QuantitySearchParam
                return isDescending
                    ? query.OrderByDescending(r =>
                        _context.QuantitySearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == GetSearchParamIdFromUrl(sortExpr.Parameter.Url))
                            .Select(sp => sp.LowValue)
                            .Max())
                    : query.OrderBy(r =>
                        _context.QuantitySearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == GetSearchParamIdFromUrl(sortExpr.Parameter.Url))
                            .Select(sp => sp.LowValue)
                            .Min());

            case SearchParamType.Reference:
                // LEFT JOIN with ReferenceSearchParam, sort by ReferenceResourceId
                return isDescending
                    ? query.OrderByDescending(r =>
                        _context.ReferenceSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == GetSearchParamIdFromUrl(sortExpr.Parameter.Url))
                            .Select(sp => sp.ReferenceResourceId)
                            .Max())
                    : query.OrderBy(r =>
                        _context.ReferenceSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == GetSearchParamIdFromUrl(sortExpr.Parameter.Url))
                            .Select(sp => sp.ReferenceResourceId)
                            .Min());

            case SearchParamType.Uri:
                // LEFT JOIN with UriSearchParam
                return isDescending
                    ? query.OrderByDescending(r =>
                        _context.UriSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == GetSearchParamIdFromUrl(sortExpr.Parameter.Url))
                            .Select(sp => sp.Uri)
                            .Max())
                    : query.OrderBy(r =>
                        _context.UriSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == GetSearchParamIdFromUrl(sortExpr.Parameter.Url))
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
    /// Helper method to get search parameter ID from URL.
    /// Uses synchronous cache lookup. Parameter IDs are pre-cached during startup by IndexLoaderService.
    /// For missing parameters, returns 0 (no matches - lenient fallback).
    /// </summary>
    private short GetSearchParamIdFromUrl(Uri url)
    {
        if (url == null)
        {
            _logger.LogWarning("SearchParam URL is null, sort will have no effect");
            return 0;
        }

        // Convert Uri to string for database query
        string urlString = url.ToString();

        // Query SearchParam table synchronously (EF Core handles this efficiently with caching)
        // Note: This executes once per sort parameter, cached at SQL Server level
        var searchParam = _context.SearchParams
            .AsNoTracking()
            .FirstOrDefault(sp => sp.Uri == urlString);

        if (searchParam == null)
        {
            _logger.LogWarning("SearchParam not found for URL: {Url}, sort will have no effect", urlString);
            return 0; // Return 0 - no matches (lenient behavior)
        }

        return searchParam.SearchParamId;
    }

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
        var paramType = sortExpr.Parameter.Type;

        switch (paramType)
        {
            case SearchParamType.String:
                // LEFT JOIN with StringSearchParam, use MIN/MAX aggregation for multi-value parameters
                return isDescending
                    ? query.ThenByDescending(r =>
                        _context.StringSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == GetSearchParamIdFromUrl(sortExpr.Parameter.Url))
                            .Select(sp => sp.Text)
                            .Max())
                    : query.ThenBy(r =>
                        _context.StringSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == GetSearchParamIdFromUrl(sortExpr.Parameter.Url))
                            .Select(sp => sp.Text)
                            .Min());

            case SearchParamType.Date:
                // LEFT JOIN with DateTimeSearchParam
                return isDescending
                    ? query.ThenByDescending(r =>
                        _context.DateTimeSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == GetSearchParamIdFromUrl(sortExpr.Parameter.Url))
                            .Select(sp => sp.StartDateTime)
                            .Max())
                    : query.ThenBy(r =>
                        _context.DateTimeSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == GetSearchParamIdFromUrl(sortExpr.Parameter.Url))
                            .Select(sp => sp.StartDateTime)
                            .Min());

            case SearchParamType.Token:
                // LEFT JOIN with TokenSearchParam, sort by Code
                return isDescending
                    ? query.ThenByDescending(r =>
                        _context.TokenSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == GetSearchParamIdFromUrl(sortExpr.Parameter.Url))
                            .Select(sp => sp.Code)
                            .Max())
                    : query.ThenBy(r =>
                        _context.TokenSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == GetSearchParamIdFromUrl(sortExpr.Parameter.Url))
                            .Select(sp => sp.Code)
                            .Min());

            case SearchParamType.Number:
                // LEFT JOIN with NumberSearchParam
                return isDescending
                    ? query.ThenByDescending(r =>
                        _context.NumberSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == GetSearchParamIdFromUrl(sortExpr.Parameter.Url))
                            .Select(sp => sp.LowValue)
                            .Max())
                    : query.ThenBy(r =>
                        _context.NumberSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == GetSearchParamIdFromUrl(sortExpr.Parameter.Url))
                            .Select(sp => sp.LowValue)
                            .Min());

            case SearchParamType.Quantity:
                // LEFT JOIN with QuantitySearchParam
                return isDescending
                    ? query.ThenByDescending(r =>
                        _context.QuantitySearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == GetSearchParamIdFromUrl(sortExpr.Parameter.Url))
                            .Select(sp => sp.LowValue)
                            .Max())
                    : query.ThenBy(r =>
                        _context.QuantitySearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == GetSearchParamIdFromUrl(sortExpr.Parameter.Url))
                            .Select(sp => sp.LowValue)
                            .Min());

            case SearchParamType.Reference:
                // LEFT JOIN with ReferenceSearchParam, sort by ReferenceResourceId
                return isDescending
                    ? query.ThenByDescending(r =>
                        _context.ReferenceSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == GetSearchParamIdFromUrl(sortExpr.Parameter.Url))
                            .Select(sp => sp.ReferenceResourceId)
                            .Max())
                    : query.ThenBy(r =>
                        _context.ReferenceSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == GetSearchParamIdFromUrl(sortExpr.Parameter.Url))
                            .Select(sp => sp.ReferenceResourceId)
                            .Min());

            case SearchParamType.Uri:
                // LEFT JOIN with UriSearchParam
                return isDescending
                    ? query.ThenByDescending(r =>
                        _context.UriSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == GetSearchParamIdFromUrl(sortExpr.Parameter.Url))
                            .Select(sp => sp.Uri)
                            .Max())
                    : query.ThenBy(r =>
                        _context.UriSearchParams
                            .Where(sp => sp.ResourceSurrogateId == r.ResourceSurrogateId
                                      && sp.SearchParamId == GetSearchParamIdFromUrl(sortExpr.Parameter.Url))
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
    /// </summary>
    private IQueryable<ResourceEntity> BuildIncludeQuery(
        SearchOptions options,
        IncludeExpression includeExpr,
        short resourceTypeId)
    {
        // Get SearchParamId from the reference search parameter URL
        short searchParamId = GetSearchParamIdFromUrl(includeExpr.ReferenceSearchParameter.Url);
        if (searchParamId == 0)
        {
            _logger.LogWarning("Search parameter not found for include: {Parameter}", includeExpr.ReferenceSearchParameter.Code);
            return _context.Resources.Where(r => false);  // Return empty
        }

        // Step 1: Build main query as CTE (replicate filters, sort, and pagination)
        // Get the base query with same filters as main search
        IQueryable<ResourceEntity> baseQuery = BuildQueryAsync(options, resourceTypeId, CancellationToken.None).GetAwaiter().GetResult();

        // Find resources referenced by these main results using a subquery
        // This keeps everything in the database query (no client-side materialization)
        var referencedTypeAndIds = _context.ReferenceSearchParams
            .Where(rsp => baseQuery.Select(r => r.ResourceSurrogateId).Contains(rsp.ResourceSurrogateId) && rsp.SearchParamId == searchParamId)
            .Select(rsp => new { rsp.ReferenceResourceTypeId, rsp.ReferenceResourceId })
            .Distinct();

        // Get actual resources for these references using subquery join
        return _context.Resources
            .Where(r => !r.IsHistory && !r.IsDeleted &&
                        referencedTypeAndIds.Any(x => x.ReferenceResourceTypeId == r.ResourceTypeId && x.ReferenceResourceId == r.ResourceId))
            .Include(r => r.Transaction)
            .Distinct()
            .OrderBy(r => r.ResourceSurrogateId);  // Stable order for deduplication
    }

    /// <summary>
    /// Builds a CTE-based revinclude query that finds resources referencing the main results.
    /// Uses SQL Common Table Expression (CTE) pattern.
    /// Implements DISTINCT to deduplicate resources that reference multiple main results.
    /// </summary>
    private IQueryable<ResourceEntity> BuildRevIncludeQuery(
        SearchOptions options,
        IncludeExpression revIncludeExpr,
        short resourceTypeId)
    {
        // Get source resource type ID (e.g., Encounter for _revinclude=Encounter:subject)
        short? sourceResourceTypeId = GetResourceTypeIdAsync(revIncludeExpr.SourceResourceType, CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
        if (!sourceResourceTypeId.HasValue)
        {
            _logger.LogWarning("Source resource type not found for revinclude: {SourceType}", revIncludeExpr.SourceResourceType);
            return _context.Resources.Where(r => false);  // Return empty
        }

        // Get SearchParamId from the reference search parameter URL
        short searchParamId = GetSearchParamIdFromUrl(revIncludeExpr.ReferenceSearchParameter.Url);
        if (searchParamId == 0)
        {
            _logger.LogWarning("Search parameter not found for revinclude: {Parameter}", revIncludeExpr.ReferenceSearchParameter.Code);
            return _context.Resources.Where(r => false);  // Return empty
        }

        // Build main query using BuildQueryAsync (already includes filters, sorting, pagination)
        IQueryable<ResourceEntity> baseQuery = BuildQueryAsync(options, resourceTypeId, CancellationToken.None)
            .GetAwaiter().GetResult();

        // Find resources that reference these main results using a subquery
        // Filter by source resource type (e.g., Encounter), search parameter, and reference target
        // This keeps everything in the database query (no client-side materialization)
        var referencingRsps = _context.ReferenceSearchParams
            .Where(rsp => rsp.ResourceTypeId == sourceResourceTypeId.Value &&
                          rsp.SearchParamId == searchParamId &&
                          baseQuery.Any(mr => mr.ResourceTypeId == rsp.ReferenceResourceTypeId && mr.ResourceId == rsp.ReferenceResourceId))
            .Select(rsp => rsp.ResourceSurrogateId)
            .Distinct();

        // Get the actual resources using subquery containment
        return _context.Resources
            .Where(r => referencingRsps.Contains(r.ResourceSurrogateId) && !r.IsHistory && !r.IsDeleted)
            .Include(r => r.Transaction)
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
}
