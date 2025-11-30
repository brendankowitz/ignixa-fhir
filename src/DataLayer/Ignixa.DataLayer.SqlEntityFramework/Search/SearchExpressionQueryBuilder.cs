// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.DataLayer.SqlEntityFramework.Search;

/// <summary>
/// Builds EF Core queries from FHIR search expressions.
/// Translates the search expression tree into LINQ queries against search parameter tables.
/// </summary>
public class SearchExpressionQueryBuilder
{
    private readonly FhirDbContext _context;
    private readonly SearchParameterQueryGenerator _parameterQueryGenerator;
    private readonly ChainedExpressionProcessor _chainedExpressionProcessor;
    private readonly CompartmentSearchQueryGenerator _compartmentQueryGenerator;
    private readonly PatientEverythingQueryGenerator _patientEverythingQueryGenerator;
    private readonly ILogger<SearchExpressionQueryBuilder> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchExpressionQueryBuilder"/> class.
    /// </summary>
    /// <param name="context">The EF Core DbContext.</param>
    /// <param name="parameterQueryGenerator">The parameter query generator.</param>
    /// <param name="chainedExpressionProcessor">The chained expression processor.</param>
    /// <param name="compartmentQueryGenerator">The compartment query generator.</param>
    /// <param name="patientEverythingQueryGenerator">The patient everything query generator.</param>
    /// <param name="logger">Logger instance.</param>
    public SearchExpressionQueryBuilder(
        FhirDbContext context,
        SearchParameterQueryGenerator parameterQueryGenerator,
        ChainedExpressionProcessor chainedExpressionProcessor,
        CompartmentSearchQueryGenerator compartmentQueryGenerator,
        PatientEverythingQueryGenerator patientEverythingQueryGenerator,
        ILogger<SearchExpressionQueryBuilder> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _parameterQueryGenerator = parameterQueryGenerator ?? throw new ArgumentNullException(nameof(parameterQueryGenerator));
        _chainedExpressionProcessor = chainedExpressionProcessor ?? throw new ArgumentNullException(nameof(chainedExpressionProcessor));
        _compartmentQueryGenerator = compartmentQueryGenerator ?? throw new ArgumentNullException(nameof(compartmentQueryGenerator));
        _patientEverythingQueryGenerator = patientEverythingQueryGenerator ?? throw new ArgumentNullException(nameof(patientEverythingQueryGenerator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Applies a search expression to a base query, returning filtered results.
    /// </summary>
    /// <param name="baseQuery">The base query for resources.</param>
    /// <param name="resourceTypeId">The resource type identifier.</param>
    /// <param name="expression">The search expression to apply.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A filtered query.</returns>
    public async Task<IQueryable<ResourceEntity>> ApplySearchExpressionAsync(
        IQueryable<ResourceEntity> baseQuery,
        short resourceTypeId,
        Expression expression,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(expression);

        return expression switch
        {
            MultiaryExpression multiaryExpr => await ApplyMultiaryExpressionAsync(baseQuery, resourceTypeId, multiaryExpr, ct),
            SearchParameterExpression searchParamExpr => await ApplySearchParameterExpressionAsync(baseQuery, resourceTypeId, searchParamExpr, ct),
            ChainedExpression chainedExpr => await ApplyChainedExpressionAsync(baseQuery, resourceTypeId, chainedExpr, ct),
            CompartmentSearchExpression compartmentExpr => await ApplyCompartmentSearchExpressionAsync(baseQuery, resourceTypeId, compartmentExpr, ct),
            PatientEverythingExpression everythingExpr => await ApplyPatientEverythingExpressionAsync(baseQuery, resourceTypeId, everythingExpr, ct),
            UnionExpression unionExpr => await ApplyUnionExpressionAsync(baseQuery, resourceTypeId, unionExpr, ct),
            NotExpression notExpr => await ApplyNotExpressionAsync(baseQuery, resourceTypeId, notExpr, ct),
            MissingSearchParameterExpression missingExpr => await ApplyMissingSearchParameterExpressionAsync(baseQuery, resourceTypeId, missingExpr, ct),
            _ => throw new NotSupportedException($"Expression type {expression.GetType().Name} is not supported")
        };
    }

    private async Task<IQueryable<ResourceEntity>> ApplyMultiaryExpressionAsync(
        IQueryable<ResourceEntity> baseQuery,
        short resourceTypeId,
        MultiaryExpression expression,
        CancellationToken ct)
    {
        if (expression.Expressions.Count == 0)
        {
            return baseQuery;
        }

        // Process each sub-expression
        var queries = new List<IQueryable<long>>();
        foreach (var subExpr in expression.Expressions)
        {
            var subQuery = await ApplySearchExpressionAsync(baseQuery, resourceTypeId, subExpr, ct);
            queries.Add(subQuery.Select(r => r.ResourceSurrogateId));
        }

        // Combine based on operator
        IQueryable<long> combinedQuery = expression.MultiaryOperation switch
        {
            MultiaryOperator.And => CombineWithAnd(queries),
            MultiaryOperator.Or => CombineWithOr(queries),
            _ => throw new NotSupportedException($"Multiary operator {expression.MultiaryOperation} is not supported")
        };

        // Filter base query by combined resource IDs
        return baseQuery.Where(r => combinedQuery.Contains(r.ResourceSurrogateId));
    }

    private async Task<IQueryable<ResourceEntity>> ApplySearchParameterExpressionAsync(
        IQueryable<ResourceEntity> baseQuery,
        short resourceTypeId,
        SearchParameterExpression expression,
        CancellationToken ct)
    {
        // Generate query for this search parameter
        var matchingResourceIds = await _parameterQueryGenerator.GenerateQueryAsync(
            resourceTypeId,
            expression,
            ct);

        // Filter base query by matching resource IDs
        return baseQuery.Where(r => matchingResourceIds.Contains(r.ResourceSurrogateId));
    }

    private async Task<IQueryable<ResourceEntity>> ApplyChainedExpressionAsync(
        IQueryable<ResourceEntity> baseQuery,
        short resourceTypeId,
        ChainedExpression expression,
        CancellationToken ct)
    {
        // Process chained expression to get matching resource IDs
        var matchingResourceIds = await _chainedExpressionProcessor.ProcessChainAsync(
            resourceTypeId,
            expression,
            ct);

        // Filter base query by matching resource IDs
        return baseQuery.Where(r => matchingResourceIds.Contains(r.ResourceSurrogateId));
    }

    private async Task<IQueryable<ResourceEntity>> ApplyCompartmentSearchExpressionAsync(
        IQueryable<ResourceEntity> baseQuery,
        short resourceTypeId,
        CompartmentSearchExpression expression,
        CancellationToken ct)
    {
        _logger.LogDebug(
            "Processing compartment search: {CompartmentType}/{CompartmentId} with resource types: [{ResourceTypes}]",
            expression.CompartmentType,
            expression.CompartmentId,
            expression.FilteredResourceTypes.Count > 0 ? string.Join(",", expression.FilteredResourceTypes) : "all");

        // Use optimized compartment query generator to get matching resource IDs
        // Pass filtered resource types if specified (e.g., /Patient/example/Observation or /Patient/example/*?_type=Observation)
        // Pass null if wildcard search to get all types in the compartment
        IReadOnlyCollection<string>? resourceTypesToSearch = expression.FilteredResourceTypes.Count > 0
            ? (IReadOnlyCollection<string>)expression.FilteredResourceTypes
            : null;

        var matchingResourceIds = await _compartmentQueryGenerator.GenerateCompartmentQueryAsync(
            expression.CompartmentType,
            expression.CompartmentId,
            resourceTypesToSearch,
            ct);

        // Filter base query by matching resource IDs
        return baseQuery.Where(r => matchingResourceIds.Contains(r.ResourceSurrogateId));
    }

    private async Task<IQueryable<ResourceEntity>> ApplyPatientEverythingExpressionAsync(
        IQueryable<ResourceEntity> baseQuery,
        short resourceTypeId,
        PatientEverythingExpression expression,
        CancellationToken ct)
    {
        _logger.LogDebug(
            "Processing Patient $everything expression for {PatientCount} patient(s)",
            expression.PatientIds.Count);

        // Use PatientEverythingQueryGenerator to build the optimized query
        var matchingResourceIds = await _patientEverythingQueryGenerator.GeneratePatientEverythingQueryAsync(
            expression,
            ct);

        // Filter base query by matching resource IDs
        return baseQuery.Where(r => matchingResourceIds.Contains(r.ResourceSurrogateId));
    }

    private async Task<IQueryable<ResourceEntity>> ApplyUnionExpressionAsync(
        IQueryable<ResourceEntity> baseQuery,
        short resourceTypeId,
        UnionExpression expression,
        CancellationToken ct)
    {
        // Build a UNION query from all sub-expressions without materializing
        IQueryable<ResourceEntity>? unionedQuery = null;

        foreach (var subExpr in expression.Expressions)
        {
            var filteredQuery = await ApplySearchExpressionAsync(baseQuery, resourceTypeId, subExpr, ct);

            if (unionedQuery == null)
            {
                unionedQuery = filteredQuery;
            }
            else
            {
                // UNION with previous queries
                unionedQuery = unionedQuery.Union(filteredQuery);
            }
        }

        return unionedQuery ?? baseQuery.Where(r => false); // Return empty if no expressions
    }

    private async Task<IQueryable<ResourceEntity>> ApplyNotExpressionAsync(
        IQueryable<ResourceEntity> baseQuery,
        short resourceTypeId,
        NotExpression expression,
        CancellationToken ct)
    {
        _logger.LogDebug("Applying NOT expression");

        // Get resource IDs matching the inner expression
        var innerQuery = await ApplySearchExpressionAsync(baseQuery, resourceTypeId, expression.Expression, ct);
        var matchingResourceIds = innerQuery.Select(r => r.ResourceSurrogateId);

        // Return base query excluding the matching IDs (NOT logic)
        return baseQuery.Where(r => !matchingResourceIds.Contains(r.ResourceSurrogateId));
    }

    private Task<IQueryable<ResourceEntity>> ApplyMissingSearchParameterExpressionAsync(
        IQueryable<ResourceEntity> baseQuery,
        short resourceTypeId,
        MissingSearchParameterExpression expression,
        CancellationToken ct)
    {
        _logger.LogDebug("Applying MISSING expression for parameter: {Parameter}, IsMissing: {IsMissing}",
            expression.Parameter?.Code,
            expression.IsMissing);

        // Get the search parameter info to determine which table to query
        var searchParamInfo = expression.Parameter;
        if (searchParamInfo == null)
        {
            _logger.LogWarning("Missing search parameter expression has no parameter info");
            return Task.FromResult(baseQuery.Where(r => false)); // Return empty
        }

        // Query the appropriate search parameter table based on parameter type
        IQueryable<long> resourcesWithParameter;

        switch (searchParamInfo.Type)
        {
            case SearchParamType.String:
                resourcesWithParameter = _context.StringSearchParams
                    .Where(sp => sp.ResourceTypeId == resourceTypeId)
                    .Select(sp => sp.ResourceSurrogateId)
                    .Distinct();
                break;

            case SearchParamType.Token:
                resourcesWithParameter = _context.TokenSearchParams
                    .Where(sp => sp.ResourceTypeId == resourceTypeId)
                    .Select(sp => sp.ResourceSurrogateId)
                    .Distinct();
                break;

            case SearchParamType.Reference:
                resourcesWithParameter = _context.ReferenceSearchParams
                    .Where(sp => sp.ResourceTypeId == resourceTypeId)
                    .Select(sp => sp.ResourceSurrogateId)
                    .Distinct();
                break;

            case SearchParamType.Number:
                resourcesWithParameter = _context.NumberSearchParams
                    .Where(sp => sp.ResourceTypeId == resourceTypeId)
                    .Select(sp => sp.ResourceSurrogateId)
                    .Distinct();
                break;

            case SearchParamType.Date:
                resourcesWithParameter = _context.DateTimeSearchParams
                    .Where(sp => sp.ResourceTypeId == resourceTypeId)
                    .Select(sp => sp.ResourceSurrogateId)
                    .Distinct();
                break;

            case SearchParamType.Quantity:
                resourcesWithParameter = _context.QuantitySearchParams
                    .Where(sp => sp.ResourceTypeId == resourceTypeId)
                    .Select(sp => sp.ResourceSurrogateId)
                    .Distinct();
                break;

            case SearchParamType.Uri:
                resourcesWithParameter = _context.UriSearchParams
                    .Where(sp => sp.ResourceTypeId == resourceTypeId)
                    .Select(sp => sp.ResourceSurrogateId)
                    .Distinct();
                break;

            default:
                _logger.LogWarning("Unsupported search parameter type for missing modifier: {Type}", searchParamInfo.Type);
                return Task.FromResult(baseQuery.Where(r => false)); // Return empty
        }

        IQueryable<ResourceEntity> result;
        if (expression.IsMissing)
        {
            // Return resources that do NOT have this parameter indexed
            result = baseQuery.Where(r => !resourcesWithParameter.Contains(r.ResourceSurrogateId));
        }
        else
        {
            // Return resources that HAVE this parameter indexed
            result = baseQuery.Where(r => resourcesWithParameter.Contains(r.ResourceSurrogateId));
        }

        return Task.FromResult(result);
    }

    private static IQueryable<long> CombineWithAnd(List<IQueryable<long>> queries)
    {
        if (queries.Count == 0)
        {
            throw new ArgumentException("Cannot combine zero queries", nameof(queries));
        }

        // Start with first query
        var result = queries[0];

        // Intersect with remaining queries (AND logic)
        for (int i = 1; i < queries.Count; i++)
        {
            result = result.Intersect(queries[i]);
        }

        return result;
    }

    private static IQueryable<long> CombineWithOr(List<IQueryable<long>> queries)
    {
        if (queries.Count == 0)
        {
            throw new ArgumentException("Cannot combine zero queries", nameof(queries));
        }

        // Start with first query
        var result = queries[0];

        // Union with remaining queries (OR logic)
        for (int i = 1; i < queries.Count; i++)
        {
            result = result.Union(queries[i]);
        }

        return result;
    }
}
