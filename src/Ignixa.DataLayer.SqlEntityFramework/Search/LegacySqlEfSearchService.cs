// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    /// <param name="logger">Logger instance.</param>
    public SqlEntityFrameworkSearchService(
        FhirDbContext context,
        IFhirRepository repository,
        SearchExpressionQueryBuilder queryBuilder,
        IncludeProcessor includeProcessor,
        RevIncludeProcessor revIncludeProcessor,
        IterateProcessor iterateProcessor,
        ILogger<SqlEntityFrameworkSearchService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _queryBuilder = queryBuilder ?? throw new ArgumentNullException(nameof(queryBuilder));
        _includeProcessor = includeProcessor ?? throw new ArgumentNullException(nameof(includeProcessor));
        _revIncludeProcessor = revIncludeProcessor ?? throw new ArgumentNullException(nameof(revIncludeProcessor));
        _iterateProcessor = iterateProcessor ?? throw new ArgumentNullException(nameof(iterateProcessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<SearchEntryResult>> SearchAsync<TSearchOptions>(
        TSearchOptions searchOptions,
        CancellationToken ct = default)
        where TSearchOptions : class
    {
        // Cast to SearchOptions (we only support this type for now)
        if (searchOptions is not SearchOptions options)
        {
            throw new ArgumentException($"Search options must be of type {nameof(SearchOptions)}", nameof(searchOptions));
        }

        _logger.LogDebug("Searching for {ResourceType}", options.ResourceType);

        var results = await SearchInternalAsync(options, ct);
        return results.AsReadOnly();
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

        _logger.LogDebug("Streaming search for {ResourceType}", options.ResourceType);

        // Get ResourceTypeId
        var resourceTypeId = await GetResourceTypeIdAsync(options.ResourceType, ct);
        if (!resourceTypeId.HasValue)
        {
            _logger.LogWarning("ResourceType not found: {ResourceType}", options.ResourceType);
            yield break;
        }

        // Build query
        var query = await BuildQueryAsync(options, resourceTypeId.Value, ct);

        _logger.LogInformation("Executing streaming query for {ResourceType}\n{SQL}", options.ResourceType, query.ToQueryString());

        // Stream results - return SearchEntryResult directly (raw bytes for zero-copy serialization)
        await foreach (var entity in query.AsAsyncEnumerable().WithCancellation(ct))
        {
            var key = new ResourceKey(options.ResourceType, entity.ResourceId, entity.Version.ToString());
            var searchResult = await _repository.GetAsync(key, ct);
            if (searchResult != null)
            {
                yield return searchResult;
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

    private async Task<List<SearchEntryResult>> SearchInternalAsync(SearchOptions options, CancellationToken ct)
    {
        // Get ResourceTypeId
        var resourceTypeId = await GetResourceTypeIdAsync(options.ResourceType, ct);
        if (!resourceTypeId.HasValue)
        {
            _logger.LogWarning("ResourceType not found: {ResourceType}", options.ResourceType);
            return new List<SearchEntryResult>();
        }

        // Build query with pagination
        var query = await BuildQueryAsync(options, resourceTypeId.Value, ct);

        // Apply pagination
        var pageSize = options.MaxItemCount > 0 ? options.MaxItemCount : 10;
        var paginatedQuery = query.Take(pageSize);

        // Execute query
        var resourceEntities = await paginatedQuery
            .Select(r => new { r.ResourceId, r.Version })
            .ToListAsync(ct);

        _logger.LogDebug("Found {Count} resources", resourceEntities.Count);

        // Fetch full resources from repository - return raw bytes for zero-copy serialization
        var resources = new List<SearchEntryResult>();
        foreach (var entity in resourceEntities)
        {
            var key = new ResourceKey(options.ResourceType, entity.ResourceId, entity.Version.ToString());
            var searchResult = await _repository.GetAsync(key, ct);
            if (searchResult != null)
            {
                resources.Add(searchResult);
            }
        }

        // Process _include expressions
        if (options.Include.Count > 0)
        {
            _logger.LogDebug("Processing {IncludeCount} _include expressions", options.Include.Count);
            var includedResources = await _includeProcessor.ProcessIncludesAsync(
                resources,
                options.Include,
                ct);

            resources.AddRange(includedResources);
            _logger.LogDebug("Added {IncludedCount} included resources", includedResources.Count);
        }

        // Process _revinclude expressions
        if (options.RevInclude.Count > 0)
        {
            _logger.LogDebug("Processing {RevIncludeCount} _revinclude expressions", options.RevInclude.Count);
            var revIncludedResources = await _revIncludeProcessor.ProcessRevIncludesAsync(
                resources,
                options.RevInclude,
                ct);

            resources.AddRange(revIncludedResources);
            _logger.LogDebug("Added {RevIncludedCount} reverse included resources", revIncludedResources.Count);
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
                resources,
                allIterateExpressions,
                ct);

            resources.AddRange(iteratedResources);
            _logger.LogDebug("Added {IteratedCount} iterated resources", iteratedResources.Count);
        }

        return resources;
    }

    private async Task<IQueryable<ResourceEntity>> BuildQueryAsync(
        SearchOptions options,
        short resourceTypeId,
        CancellationToken ct)
    {
        // Start with base query for current (non-history, non-deleted) resources
        var baseQuery = _context.Resources
            .Where(r => r.ResourceTypeId == resourceTypeId
                && !r.IsHistory
                && !r.IsDeleted);

        // Apply search expression filters
        IQueryable<ResourceEntity> filteredQuery;
        if (options.Expression != null)
        {
            filteredQuery = await _queryBuilder.ApplySearchExpressionAsync(
                baseQuery,
                resourceTypeId,
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
