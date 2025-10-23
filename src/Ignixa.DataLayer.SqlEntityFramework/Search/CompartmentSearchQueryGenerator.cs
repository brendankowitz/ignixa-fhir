// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.Search.Definition;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.DataLayer.SqlEntityFramework.Search;

/// <summary>
/// Generates optimized EF Core queries for FHIR compartment searches.
/// Bypasses the expression tree to query ReferenceSearchParam directly with SearchParamId,
/// generating flat UNION operations instead of nested subqueries.
/// Matches Microsoft FHIR Server's proven fast pattern.
/// </summary>
public class CompartmentSearchQueryGenerator
{
    private readonly FhirDbContext _context;
    private readonly SearchIndexReferenceDataCache _cache;
    private readonly ICompartmentDefinitionManager _compartmentDefinitionManager;
    private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
    private readonly ILogger<CompartmentSearchQueryGenerator> _logger;

    public CompartmentSearchQueryGenerator(
        FhirDbContext context,
        SearchIndexReferenceDataCache cache,
        ICompartmentDefinitionManager compartmentDefinitionManager,
        ISearchParameterDefinitionManager searchParameterDefinitionManager,
        ILogger<CompartmentSearchQueryGenerator> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _compartmentDefinitionManager = compartmentDefinitionManager ?? throw new ArgumentNullException(nameof(compartmentDefinitionManager));
        _searchParameterDefinitionManager = searchParameterDefinitionManager ?? throw new ArgumentNullException(nameof(searchParameterDefinitionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Generates an optimized query for wildcard compartment searches.
    /// Queries ReferenceSearchParam directly by SearchParamId, avoiding nested subqueries.
    /// </summary>
    /// <param name="compartmentType">The compartment type (e.g., "Patient").</param>
    /// <param name="compartmentId">The compartment ID (e.g., "example-123").</param>
    /// <param name="resourceTypesToSearch">Resource types to include in search (null = all types in compartment).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A queryable of matching resource surrogate IDs.</returns>
    public async Task<IQueryable<long>> GenerateCompartmentQueryAsync(
        string compartmentType,
        string compartmentId,
        IReadOnlyCollection<string>? resourceTypesToSearch,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(compartmentType);
        ArgumentNullException.ThrowIfNull(compartmentId);

        _logger.LogDebug(
            "Generating optimized compartment query: {CompartmentType}/{CompartmentId} for resource types [{ResourceTypes}]",
            compartmentType,
            compartmentId,
            resourceTypesToSearch == null ? "*" : string.Join(", ", resourceTypesToSearch));

        // Parse compartment type
        if (!Enum.TryParse<CompartmentType>(compartmentType, out var compartmentTypeEnum))
        {
            throw new InvalidOperationException($"Invalid compartment type: {compartmentType}");
        }

        // Get compartment resource types
        if (!_compartmentDefinitionManager.TryGetResourceTypes(compartmentTypeEnum, out var allResourceTypes))
        {
            _logger.LogWarning("No resource types found for compartment: {CompartmentType}", compartmentType);
            return Enumerable.Empty<long>().AsQueryable();
        }

        // Determine which resource types to search
        var resourceTypesToUse = resourceTypesToSearch == null || resourceTypesToSearch.Count == 0
            ? allResourceTypes.ToList()
            : allResourceTypes.Where(rt => resourceTypesToSearch.Contains(rt)).ToList();

        if (resourceTypesToUse.Count == 0)
        {
            _logger.LogWarning("No matching resource types for compartment search");
            return Enumerable.Empty<long>().AsQueryable();
        }

        _logger.LogDebug("Resource types for compartment search: [{ResourceTypes}]", string.Join(", ", resourceTypesToUse));

        // Build union of ReferenceSearchParam queries grouped by SearchParamId
        // Key optimization: Group by search parameter first, then collect all resource types that use it
        // This eliminates redundant UNION branches when same parameter applies to multiple resource types
        // e.g., "subject" parameter applies to Patient, Observation, Condition - create ONE query with IN clause
        IQueryable<long>? unionedQuery = null;

        // Build map of SearchParamUri -> (SearchParamId, Set<ResourceTypeId>)
        // This allows us to create a single query per unique search parameter with IN clause for all applicable types
        var searchParamMap = new Dictionary<string, (short searchParamId, HashSet<short> resourceTypeIds)>();

        // Get all resource types for quick lookup
        var resourceTypeMap = await _context.ResourceTypes
            .AsNoTracking()
            .Where(rt => resourceTypesToUse.Contains(rt.Name))
            .ToDictionaryAsync(rt => rt.Name, rt => rt.ResourceTypeId, ct);

        // First pass: Collect all search parameters and their resource types
        foreach (var resourceType in resourceTypesToUse)
        {
            if (!_compartmentDefinitionManager.TryGetSearchParams(resourceType, compartmentTypeEnum, out var searchParams))
            {
                continue;
            }

            if (!resourceTypeMap.TryGetValue(resourceType, out var resourceTypeId))
            {
                _logger.LogDebug("ResourceType not found in cache: {ResourceType}", resourceType);
                continue;
            }

            foreach (var searchParamCode in searchParams)
            {
                try
                {
                    // Get search parameter info from definition manager
                    var searchParamInfo = _searchParameterDefinitionManager.GetSearchParameter(resourceType, searchParamCode);

                    // Get SearchParamId from cache using the parameter URI
                    var searchParamUri = searchParamInfo.Url.ToString();
                    var searchParamId = await _cache.GetSearchParamIdAsync(searchParamUri);
                    if (!searchParamId.HasValue)
                    {
                        _logger.LogDebug("SearchParamId not found for URI: {Uri}", searchParamUri);
                        continue;
                    }

                    // Add to map, grouping all resource types that use this parameter
                    if (!searchParamMap.ContainsKey(searchParamUri))
                    {
                        searchParamMap[searchParamUri] = (searchParamId.Value, new HashSet<short>());
                    }

                    searchParamMap[searchParamUri].resourceTypeIds.Add(resourceTypeId);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(
                        ex,
                        "Failed to process search parameter {ResourceType}:{SearchParamCode}",
                        resourceType,
                        searchParamCode);
                    continue;
                }
            }
        }

        // Second pass: Create queries for each search parameter, avoiding .Contains() on collections
        // to prevent EF Core from serializing to OPENJSON.
        // Instead, we iterate through resource types explicitly and UNION the results.
        // This avoids OPENJSON parameter serialization while still grouping by SearchParamId.
        foreach (var (searchParamUri, (searchParamId, resourceTypeIds)) in searchParamMap)
        {
            _logger.LogDebug(
                "Adding query for search parameter {SearchParamUri} ({SearchParamId}) for resource types [{ResourceTypes}]",
                searchParamUri,
                searchParamId,
                string.Join(", ", resourceTypeIds));

            // For each resource type that uses this search parameter, create a query with direct comparison
            // This avoids .Contains() which triggers OPENJSON, instead using WHERE ResourceTypeId = @p
            IQueryable<long>? typeQueries = null;

            foreach (var resourceTypeId in resourceTypeIds)
            {
                var typeQuery = from refParam in _context.ReferenceSearchParams
                                join resource in _context.Resources
                                    on refParam.ResourceSurrogateId equals resource.ResourceSurrogateId
                                where refParam.SearchParamId == searchParamId
                                    && refParam.ReferenceResourceId == compartmentId
                                    && refParam.ResourceTypeId == resourceTypeId
                                    && !resource.IsHistory
                                    && !resource.IsDeleted
                                select refParam.ResourceSurrogateId;

                typeQueries = typeQueries == null
                    ? typeQuery
                    : typeQueries.Union(typeQuery);
            }

            if (typeQueries != null)
            {
                unionedQuery = unionedQuery == null
                    ? typeQueries
                    : unionedQuery.Union(typeQueries);
            }
        }

        if (unionedQuery == null)
        {
            _logger.LogWarning("No search parameters found for compartment search, returning empty result");
            return Enumerable.Empty<long>().AsQueryable();
        }

        _logger.LogDebug("Compartment query generation complete, processed {ParameterCount} unique search parameters", searchParamMap.Count);

        return unionedQuery;
    }
}
