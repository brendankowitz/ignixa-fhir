// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.DataLayer.SqlEntityFramework.Search;

/// <summary>
/// Packs the parameters shared by every <see cref="IExpressionVisitor{TContext,TOutput}"/> visit
/// method dispatched from <see cref="SearchExpressionQueryBuilder"/>.
/// </summary>
public readonly record struct SqlQueryContext(
    IQueryable<ResourceEntity> BaseQuery,
    short? ResourceTypeId,
    CancellationToken CancellationToken);

/// <summary>
/// Builds EF Core queries from FHIR search expressions.
/// Translates the search expression tree into LINQ queries against search parameter tables.
/// </summary>
public sealed class SearchExpressionQueryBuilder : IExpressionVisitor<SqlQueryContext, Task<IQueryable<ResourceEntity>>>
{
    private readonly FhirDbContext _context;
    private readonly SearchParameterQueryGenerator _parameterQueryGenerator;
    private readonly ChainedExpressionProcessor _chainedExpressionProcessor;
    private readonly CompartmentSearchQueryGenerator _compartmentQueryGenerator;
    private readonly PatientEverythingQueryGenerator _patientEverythingQueryGenerator;
    private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
    private readonly ILogger<SearchExpressionQueryBuilder> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchExpressionQueryBuilder"/> class.
    /// </summary>
    /// <param name="context">The EF Core DbContext.</param>
    /// <param name="parameterQueryGenerator">The parameter query generator.</param>
    /// <param name="chainedExpressionProcessor">The chained expression processor.</param>
    /// <param name="compartmentQueryGenerator">The compartment query generator.</param>
    /// <param name="patientEverythingQueryGenerator">The patient everything query generator.</param>
    /// <param name="searchParameterDefinitionManager">The search parameter definition manager.</param>
    /// <param name="logger">Logger instance.</param>
    public SearchExpressionQueryBuilder(
        FhirDbContext context,
        SearchParameterQueryGenerator parameterQueryGenerator,
        ChainedExpressionProcessor chainedExpressionProcessor,
        CompartmentSearchQueryGenerator compartmentQueryGenerator,
        PatientEverythingQueryGenerator patientEverythingQueryGenerator,
        ISearchParameterDefinitionManager searchParameterDefinitionManager,
        ILogger<SearchExpressionQueryBuilder> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _parameterQueryGenerator = parameterQueryGenerator ?? throw new ArgumentNullException(nameof(parameterQueryGenerator));
        _chainedExpressionProcessor = chainedExpressionProcessor ?? throw new ArgumentNullException(nameof(chainedExpressionProcessor));
        _compartmentQueryGenerator = compartmentQueryGenerator ?? throw new ArgumentNullException(nameof(compartmentQueryGenerator));
        _patientEverythingQueryGenerator = patientEverythingQueryGenerator ?? throw new ArgumentNullException(nameof(patientEverythingQueryGenerator));
        _searchParameterDefinitionManager = searchParameterDefinitionManager ?? throw new ArgumentNullException(nameof(searchParameterDefinitionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Applies a search expression to a base query, returning filtered results.
    /// </summary>
    /// <param name="baseQuery">The base query for resources.</param>
    /// <param name="resourceTypeId">The resource type identifier, or null for system-wide search across all types.</param>
    /// <param name="expression">The search expression to apply.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A filtered query.</returns>
    public Task<IQueryable<ResourceEntity>> ApplySearchExpressionAsync(
        IQueryable<ResourceEntity> baseQuery,
        short? resourceTypeId,
        Expression expression,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(baseQuery);
        ArgumentNullException.ThrowIfNull(expression);

        _logger.LogDebug(
            "ApplySearchExpressionAsync: ExpressionType={ExpressionType}, ResourceTypeId={ResourceTypeId}",
            expression.GetType().Name,
            resourceTypeId);

        var context = new SqlQueryContext(baseQuery, resourceTypeId, ct);
        return expression.AcceptVisitor(this, context);
    }

    async Task<IQueryable<ResourceEntity>> IExpressionVisitor<SqlQueryContext, Task<IQueryable<ResourceEntity>>>.VisitMultiary(MultiaryExpression expression, SqlQueryContext context)
    {
        if (expression.Expressions.Count == 0)
        {
            return context.BaseQuery;
        }

        // Process each sub-expression
        var queries = new List<IQueryable<long>>();
        foreach (var subExpr in expression.Expressions)
        {
            var subQuery = await subExpr.AcceptVisitor(this, context);
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
        return context.BaseQuery.Where(r => combinedQuery.Contains(r.ResourceSurrogateId));
    }

    async Task<IQueryable<ResourceEntity>> IExpressionVisitor<SqlQueryContext, Task<IQueryable<ResourceEntity>>>.VisitSearchParameter(SearchParameterExpression expression, SqlQueryContext context)
    {
        _logger.LogDebug(
            "ApplySearchParameterExpressionAsync: ParameterCode={ParameterCode}, ParameterName={ParameterName}, ResourceTypeId={ResourceTypeId}",
            expression.Parameter?.Code,
            expression.Parameter?.Name,
            context.ResourceTypeId);

        // Generate query for this search parameter
        var matchingResourceIds = await _parameterQueryGenerator.GenerateQueryAsync(
            context.ResourceTypeId,
            expression,
            context.CancellationToken);

        _logger.LogDebug("Generated matching resource IDs query, applying to base query");

        // Filter base query by matching resource IDs
        return context.BaseQuery.Where(r => matchingResourceIds.Contains(r.ResourceSurrogateId));
    }

    async Task<IQueryable<ResourceEntity>> IExpressionVisitor<SqlQueryContext, Task<IQueryable<ResourceEntity>>>.VisitChained(ChainedExpression expression, SqlQueryContext context)
    {
        // Process chained expression to get matching resource IDs
        var matchingResourceIds = await _chainedExpressionProcessor.ProcessChainAsync(
            context.ResourceTypeId,
            expression,
            context.CancellationToken);

        // Filter base query by matching resource IDs
        return context.BaseQuery.Where(r => matchingResourceIds.Contains(r.ResourceSurrogateId));
    }

    async Task<IQueryable<ResourceEntity>> IExpressionVisitor<SqlQueryContext, Task<IQueryable<ResourceEntity>>>.VisitCompartment(CompartmentSearchExpression expression, SqlQueryContext context)
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
            context.CancellationToken);

        // Filter base query by matching resource IDs
        return context.BaseQuery.Where(r => matchingResourceIds.Contains(r.ResourceSurrogateId));
    }

    async Task<IQueryable<ResourceEntity>> IExpressionVisitor<SqlQueryContext, Task<IQueryable<ResourceEntity>>>.VisitPatientEverything(PatientEverythingExpression expression, SqlQueryContext context)
    {
        _logger.LogDebug(
            "Processing Patient $everything expression for {PatientCount} patient(s)",
            expression.PatientIds.Count);

        // Use PatientEverythingQueryGenerator to build the optimized query
        var matchingResourceIds = await _patientEverythingQueryGenerator.GeneratePatientEverythingQueryAsync(
            expression,
            context.CancellationToken);

        // Filter base query by matching resource IDs
        return context.BaseQuery.Where(r => matchingResourceIds.Contains(r.ResourceSurrogateId));
    }

    async Task<IQueryable<ResourceEntity>> IExpressionVisitor<SqlQueryContext, Task<IQueryable<ResourceEntity>>>.VisitUnion(UnionExpression expression, SqlQueryContext context)
    {
        // Build a UNION query from all sub-expressions without materializing
        IQueryable<ResourceEntity>? unionedQuery = null;

        foreach (var subExpr in expression.Expressions)
        {
            var filteredQuery = await subExpr.AcceptVisitor(this, context);

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

        return unionedQuery ?? context.BaseQuery.Where(r => false); // Return empty if no expressions
    }

    async Task<IQueryable<ResourceEntity>> IExpressionVisitor<SqlQueryContext, Task<IQueryable<ResourceEntity>>>.VisitNotExpression(NotExpression expression, SqlQueryContext context)
    {
        _logger.LogDebug("Applying NOT expression");

        // Get resource IDs matching the inner expression
        var innerQuery = await expression.Expression.AcceptVisitor(this, context);
        var matchingResourceIds = innerQuery.Select(r => r.ResourceSurrogateId);

        // Return base query excluding the matching IDs (NOT logic)
        return context.BaseQuery.Where(r => !matchingResourceIds.Contains(r.ResourceSurrogateId));
    }

    async Task<IQueryable<ResourceEntity>> IExpressionVisitor<SqlQueryContext, Task<IQueryable<ResourceEntity>>>.VisitMissingSearchParameter(MissingSearchParameterExpression expression, SqlQueryContext context)
    {
        _logger.LogDebug("Applying MISSING expression for parameter: {Parameter}, IsMissing: {IsMissing}",
            expression.Parameter?.Code,
            expression.IsMissing);

        // Get the search parameter info to determine which table to query
        var searchParamInfo = expression.Parameter;
        if (searchParamInfo == null)
        {
            _logger.LogWarning("Missing search parameter expression has no parameter info");
            return context.BaseQuery.Where(r => false); // Return empty
        }

        // Look up the SearchParamId for this specific search parameter
        short? searchParamId = await _parameterQueryGenerator.GetSearchParamIdAsync(searchParamInfo);
        if (!searchParamId.HasValue)
        {
            _logger.LogWarning("Could not find SearchParamId for parameter {Code}", searchParamInfo.Code);
            return context.BaseQuery.Where(r => false); // Return empty
        }

        _logger.LogDebug("Found SearchParamId {SearchParamId} for parameter {Code}", searchParamId.Value, searchParamInfo.Code);

        // Query the appropriate search parameter table based on parameter type
        // IMPORTANT: Filter by both ResourceTypeId AND SearchParamId to find resources
        // that have this specific parameter indexed
        IQueryable<long> resourcesWithParameter;

        switch (searchParamInfo.Type)
        {
            case SearchParamType.String:
                resourcesWithParameter = _context.StringSearchParams
                    .Where(sp => (!context.ResourceTypeId.HasValue || sp.ResourceTypeId == context.ResourceTypeId.Value)
                        && sp.SearchParamId == searchParamId.Value)
                    .Select(sp => sp.ResourceSurrogateId)
                    .Distinct();
                break;

            case SearchParamType.Token:
                resourcesWithParameter = _context.TokenSearchParams
                    .Where(sp => (!context.ResourceTypeId.HasValue || sp.ResourceTypeId == context.ResourceTypeId.Value)
                        && sp.SearchParamId == searchParamId.Value)
                    .Select(sp => sp.ResourceSurrogateId)
                    .Distinct();
                break;

            case SearchParamType.Reference:
                resourcesWithParameter = _context.ReferenceSearchParams
                    .Where(sp => (!context.ResourceTypeId.HasValue || sp.ResourceTypeId == context.ResourceTypeId.Value)
                        && sp.SearchParamId == searchParamId.Value)
                    .Select(sp => sp.ResourceSurrogateId)
                    .Distinct();
                break;

            case SearchParamType.Number:
                resourcesWithParameter = _context.NumberSearchParams
                    .Where(sp => (!context.ResourceTypeId.HasValue || sp.ResourceTypeId == context.ResourceTypeId.Value)
                        && sp.SearchParamId == searchParamId.Value)
                    .Select(sp => sp.ResourceSurrogateId)
                    .Distinct();
                break;

            case SearchParamType.Date:
                resourcesWithParameter = _context.DateTimeSearchParams
                    .Where(sp => (!context.ResourceTypeId.HasValue || sp.ResourceTypeId == context.ResourceTypeId.Value)
                        && sp.SearchParamId == searchParamId.Value)
                    .Select(sp => sp.ResourceSurrogateId)
                    .Distinct();
                break;

            case SearchParamType.Quantity:
                resourcesWithParameter = _context.QuantitySearchParams
                    .Where(sp => (!context.ResourceTypeId.HasValue || sp.ResourceTypeId == context.ResourceTypeId.Value)
                        && sp.SearchParamId == searchParamId.Value)
                    .Select(sp => sp.ResourceSurrogateId)
                    .Distinct();
                break;

            case SearchParamType.Uri:
                resourcesWithParameter = _context.UriSearchParams
                    .Where(sp => (!context.ResourceTypeId.HasValue || sp.ResourceTypeId == context.ResourceTypeId.Value)
                        && sp.SearchParamId == searchParamId.Value)
                    .Select(sp => sp.ResourceSurrogateId)
                    .Distinct();
                break;

            default:
                _logger.LogWarning("Unsupported search parameter type for missing modifier: {Type}", searchParamInfo.Type);
                return context.BaseQuery.Where(r => false); // Return empty
        }

        IQueryable<ResourceEntity> result;
        if (expression.IsMissing)
        {
            // Return resources that do NOT have this parameter indexed
            result = context.BaseQuery.Where(r => !resourcesWithParameter.Contains(r.ResourceSurrogateId));
        }
        else
        {
            // Return resources that HAVE this parameter indexed
            result = context.BaseQuery.Where(r => resourcesWithParameter.Contains(r.ResourceSurrogateId));
        }

        return result;
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

        // Use Concat+Distinct instead of chained Union to avoid deeply nested expression trees
        // Chained Union creates: q0.Union(q1).Union(q2)...Union(qN) which nests deeply and can cause
        // stack overflow in EF Core's ExpressionTreeFuncletizer with 100+ queries (e.g., ChargeItem).
        // Concat creates a flatter tree: Concat(Concat(q0, q1), q2)... then Distinct deduplicates.
        // This provides the same OR semantics (deduplicated union) with better performance.
        var result = queries.Aggregate((current, next) => current.Concat(next));
        return result.Distinct();
    }

    async Task<IQueryable<ResourceEntity>> IExpressionVisitor<SqlQueryContext, Task<IQueryable<ResourceEntity>>>.VisitNotReferenced(NotReferencedExpression expression, SqlQueryContext context)
    {
        _logger.LogDebug(
            "Processing _not-referenced expression: SourceType={SourceType}, Path={Path}",
            expression.SourceResourceType ?? "*",
            expression.ReferencePath ?? "*");

        SearchParameterInfo? searchParamInfo = null;
        if (expression.SourceResourceType is not null && expression.ReferencePath is not null)
        {
            try
            {
                var param = _searchParameterDefinitionManager.GetSearchParameter(
                    expression.SourceResourceType,
                    expression.ReferencePath);

                if (param.Type != SearchParamType.Reference)
                {
                    _logger.LogWarning(
                        "Search parameter {Path} on {Type} is not a reference type (Type={ActualType}), ignoring path filter",
                        expression.ReferencePath,
                        expression.SourceResourceType,
                        param.Type);
                }
                else
                {
                    searchParamInfo = param;
                }
            }
            catch (SearchParameterNotSupportedException)
            {
                _logger.LogDebug(
                    "Search parameter {Path} not found on {Type}, using path-agnostic query",
                    expression.ReferencePath,
                    expression.SourceResourceType);
            }
        }

        var matchingResourceIds = await _parameterQueryGenerator.GenerateNotReferencedQueryAsync(
            context.ResourceTypeId,
            expression,
            searchParamInfo,
            context.CancellationToken);

        return context.BaseQuery.Where(r => matchingResourceIds.Contains(r.ResourceSurrogateId));
    }

    Task<IQueryable<ResourceEntity>> IExpressionVisitor<SqlQueryContext, Task<IQueryable<ResourceEntity>>>.VisitBinary(BinaryExpression expression, SqlQueryContext context) =>
        throw new NotSupportedException($"{nameof(SearchExpressionQueryBuilder)} does not handle bare {nameof(BinaryExpression)} — field-level expressions are only valid nested inside a {nameof(SearchParameterExpression)}.");

    Task<IQueryable<ResourceEntity>> IExpressionVisitor<SqlQueryContext, Task<IQueryable<ResourceEntity>>>.VisitMissingField(MissingFieldExpression expression, SqlQueryContext context) =>
        throw new NotSupportedException($"{nameof(SearchExpressionQueryBuilder)} does not handle bare {nameof(MissingFieldExpression)} — field-level expressions are only valid nested inside a {nameof(SearchParameterExpression)}.");

    Task<IQueryable<ResourceEntity>> IExpressionVisitor<SqlQueryContext, Task<IQueryable<ResourceEntity>>>.VisitString(StringExpression expression, SqlQueryContext context) =>
        throw new NotSupportedException($"{nameof(SearchExpressionQueryBuilder)} does not handle bare {nameof(StringExpression)} — field-level expressions are only valid nested inside a {nameof(SearchParameterExpression)}.");

    Task<IQueryable<ResourceEntity>> IExpressionVisitor<SqlQueryContext, Task<IQueryable<ResourceEntity>>>.VisitInclude(IncludeExpression expression, SqlQueryContext context) =>
        throw new NotSupportedException($"{nameof(IncludeExpression)} is handled by {nameof(IncludeProcessor)}/{nameof(RevIncludeProcessor)}, not by {nameof(SearchExpressionQueryBuilder)}.");

    Task<IQueryable<ResourceEntity>> IExpressionVisitor<SqlQueryContext, Task<IQueryable<ResourceEntity>>>.VisitSortParameter(SortExpression expression, SqlQueryContext context) =>
        throw new NotSupportedException($"{nameof(SortExpression)} is applied to sort order separately, not through {nameof(SearchExpressionQueryBuilder)}.");

    Task<IQueryable<ResourceEntity>> IExpressionVisitor<SqlQueryContext, Task<IQueryable<ResourceEntity>>>.VisitIn<T>(InExpression<T> expression, SqlQueryContext context) =>
        throw new NotSupportedException($"{nameof(SearchExpressionQueryBuilder)} does not handle bare {nameof(InExpression<T>)} — field-level expressions are only valid nested inside a {nameof(SearchParameterExpression)}.");
}
