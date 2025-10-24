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

        // Stream results - buffer for include processing
        var mainResults = new List<SearchEntryResult>();
        await foreach (var entity in query
            .Include(x => x.Transaction)
            .AsAsyncEnumerable().WithCancellation(ct))
        {
            var searchResult = MapResourceEntityToSearchResult(entity, options.ResourceType);
            mainResults.Add(searchResult);
            yield return searchResult;  // Stream immediately
        }

        // Process includes/revincludes if requested
        if (options.Include.Count > 0 || options.RevInclude.Count > 0)
        {
            var includedResources = await ProcessIncludesAndRevIncludesAsync(mainResults, options, ct);
            foreach (var included in includedResources)
            {
                // Mark included resources with Include mode instead of Match
                yield return included with { SearchMode = SearchEntryMode.Include };
            }
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
        return ApplySorting(filteredQuery, options.Sort);
    }

    private IOrderedQueryable<ResourceEntity> ApplySorting(
        IQueryable<ResourceEntity> query,
        IReadOnlyList<SortExpression>? sortOptions)
    {
        if (sortOptions == null || sortOptions.Count == 0)
        {
            // Default sort: by ResourceSurrogateId descending (newest first)
            return query.OrderByDescending(r => r.ResourceSurrogateId);
        }

        // TODO: Implement search parameter-based sorting
        // For now, use default sort
        _logger.LogWarning("Custom sorting not yet implemented, using default sort");
        return query.OrderByDescending(r => r.ResourceSurrogateId);
    }

    private async ValueTask<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken ct)
    {
        var entity = await _context.ResourceTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(rt => rt.Name == resourceType, ct);

        return entity?.ResourceTypeId;
    }
}
