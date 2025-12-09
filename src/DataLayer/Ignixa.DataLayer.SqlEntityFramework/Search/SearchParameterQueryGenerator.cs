// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.Domain.Abstractions;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.DataLayer.SqlEntityFramework.Search;

/// <summary>
/// Generates EF Core queries for individual search parameters.
/// Maps search parameter values to queries against specific search parameter tables.
/// </summary>
public class SearchParameterQueryGenerator
{
    private readonly FhirDbContext _context;
    private readonly SearchIndexReferenceDataCache _cache;
    private readonly ILogger<SearchParameterQueryGenerator> _logger;
    private readonly CompositeSearchParameterQueryGenerator _compositeQueryGenerator;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchParameterQueryGenerator"/> class.
    /// </summary>
    /// <param name="context">The EF Core DbContext.</param>
    /// <param name="cache">The reference data cache.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="compositeQueryGenerator">The composite search parameter query generator.</param>
    public SearchParameterQueryGenerator(
        FhirDbContext context,
        SearchIndexReferenceDataCache cache,
        ILogger<SearchParameterQueryGenerator> logger,
        CompositeSearchParameterQueryGenerator compositeQueryGenerator)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _compositeQueryGenerator = compositeQueryGenerator ?? throw new ArgumentNullException(nameof(compositeQueryGenerator));
    }

    /// <summary>
    /// Gets the SearchParamId for a search parameter from the cache.
    /// </summary>
    /// <param name="searchParamInfo">The search parameter info.</param>
    /// <returns>The SearchParamId, or null if not found.</returns>
    public ValueTask<short?> GetSearchParamIdAsync(SearchParameterInfo searchParamInfo)
    {
        return _cache.GetSearchParamIdAsync(searchParamInfo);
    }

    /// <summary>
    /// Generates a query for a search parameter expression, returning matching resource surrogate IDs.
    /// </summary>
    /// <param name="resourceTypeId">The resource type identifier, or null for system-wide search across all types.</param>
    /// <param name="expression">The search parameter expression.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A queryable of matching resource surrogate IDs.</returns>
    public async Task<IQueryable<long>> GenerateQueryAsync(
        short? resourceTypeId,
        SearchParameterExpression expression,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(expression);

        try
        {
            _logger.LogDebug("Generating query for search parameter: {Parameter}", expression.Parameter?.Name);

            // Handle resource-level parameters that query Resource table directly
            // instead of indexed search parameter tables
            if (expression.Parameter?.Code == "_id")
            {
                _logger.LogDebug("Processing _id parameter expression");
                return await ProcessResourceIdExpressionAsync(resourceTypeId, expression.Expression, ct);
            }

            if (expression.Parameter?.Code == "_lastUpdated")
            {
                return await ProcessResourceLastUpdatedExpressionAsync(resourceTypeId, expression.Expression, ct);
            }

            if (expression.Parameter?.Code == "_type")
            {
                return await ProcessResourceTypeExpressionAsync(resourceTypeId, expression.Expression, ct);
            }

            // Look up SearchParamId for this search parameter - required for filtering indexed search params
            short? searchParamId = null;
            if (expression.Parameter != null)
            {
                searchParamId = await _cache.GetSearchParamIdAsync(expression.Parameter);
                if (!searchParamId.HasValue)
                {
                    _logger.LogWarning(
                        "SearchParamId not found for parameter {Code} ({Url}), search may return incorrect results",
                        expression.Parameter.Code,
                        expression.Parameter.Url);
                }
            }

            // Handle composite search parameters
            // TEMPORARILY DISABLED: Testing if this causes stack overflow
            //if (expression.Parameter?.Type == SearchParamType.Composite && searchParamId.HasValue)
            //{
            //    return await ProcessCompositeExpressionAsync(resourceTypeId, searchParamId.Value, expression.Parameter, expression.Expression, ct);
            //}

            // Process the inner expression based on its type, with SearchParamId for proper filtering
            return await ProcessExpressionAsync(resourceTypeId, searchParamId, expression.Expression, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "CRITICAL ERROR in GenerateQueryAsync - Exception during query generation BEFORE EF Core compilation. " +
                "ResourceTypeId={ResourceTypeId}, " +
                "ParameterCode={ParameterCode}, " +
                "ParameterName={ParameterName}, " +
                "ParameterType={ParameterType}, " +
                "ExpressionType={ExpressionType}, " +
                "InnerExpressionType={InnerExpressionType}",
                resourceTypeId,
                expression.Parameter?.Code,
                expression.Parameter?.Name,
                expression.Parameter?.Type,
                expression.GetType().Name,
                expression.Expression?.GetType().Name);

            throw;
        }
    }

    /// <summary>
    /// Processes composite search parameter expressions by routing to the appropriate composite table.
    /// </summary>
    private async Task<IQueryable<long>> ProcessCompositeExpressionAsync(
        short? resourceTypeId,
        short searchParamId,
        SearchParameterInfo searchParameter,
        Expression expr,
        CancellationToken ct)
    {
        _logger.LogDebug("Processing composite search parameter: {Code}", searchParameter.Code);

        // Determine the composite type based on component types
        var compositeType = _compositeQueryGenerator.DetermineCompositeType(searchParameter);

        if (compositeType == CompositeType.Unknown)
        {
            _logger.LogWarning(
                "Unknown composite type for parameter {Code}, falling back to non-composite search",
                searchParameter.Code);
            return await ProcessExpressionAsync(resourceTypeId, searchParamId, expr, ct);
        }

        // Extract component expressions from the outer expression
        // Composite expressions are typically MultiaryExpression (AND) containing component expressions
        var componentExpressions = ExtractComponentExpressions(expr);

        if (componentExpressions.Count < 2)
        {
            _logger.LogWarning(
                "Composite parameter {Code} requires at least 2 components, found {Count}",
                searchParameter.Code,
                componentExpressions.Count);
            return Enumerable.Empty<long>().AsQueryable();
        }

        // Route to appropriate composite query generator method based on type
        return compositeType switch
        {
            CompositeType.TokenToken => await _compositeQueryGenerator.GenerateTokenTokenQueryAsync(
                resourceTypeId, searchParamId, componentExpressions[0], componentExpressions[1], ct),

            CompositeType.TokenQuantity => await _compositeQueryGenerator.GenerateTokenQuantityQueryAsync(
                resourceTypeId, searchParamId, componentExpressions[0], componentExpressions[1], ct),

            CompositeType.TokenString => await _compositeQueryGenerator.GenerateTokenStringQueryAsync(
                resourceTypeId, searchParamId, componentExpressions[0], componentExpressions[1], ct),

            CompositeType.ReferenceToken => await _compositeQueryGenerator.GenerateReferenceTokenQueryAsync(
                resourceTypeId, searchParamId, componentExpressions[0], componentExpressions[1], ct),

            CompositeType.TokenDateTime => await _compositeQueryGenerator.GenerateTokenDateTimeQueryAsync(
                resourceTypeId, searchParamId, componentExpressions[0], componentExpressions[1], ct),

            _ => Enumerable.Empty<long>().AsQueryable()
        };
    }

    /// <summary>
    /// Extracts component expressions from a composite search expression.
    /// Component expressions are identified by their ComponentIndex property.
    /// </summary>
    private List<Expression> ExtractComponentExpressions(Expression expr)
    {
        var componentsByIndex = new Dictionary<int, List<Expression>>();

        void CollectByComponentIndex(Expression e)
        {
            if (e is IFieldExpression fieldExpr && fieldExpr.ComponentIndex.HasValue)
            {
                int index = fieldExpr.ComponentIndex.Value;
                if (!componentsByIndex.ContainsKey(index))
                {
                    componentsByIndex[index] = [];
                }

                componentsByIndex[index].Add(e);
            }
            else if (e is MultiaryExpression multiary)
            {
                // Check if all child expressions have the same ComponentIndex
                // If so, this is a composite component expression
                var childComponentIndices = new HashSet<int?>();
                foreach (var child in multiary.Expressions)
                {
                    if (child is IFieldExpression childField && childField.ComponentIndex.HasValue)
                    {
                        childComponentIndices.Add(childField.ComponentIndex);
                    }
                }

                if (childComponentIndices.Count == 1 && childComponentIndices.First().HasValue)
                {
                    // All children have the same ComponentIndex - this is a complete component expression
                    int index = childComponentIndices.First()!.Value;
                    if (!componentsByIndex.ContainsKey(index))
                    {
                        componentsByIndex[index] = [];
                    }

                    componentsByIndex[index].Add(e);
                }
                else
                {
                    // Mixed or no component indices - recurse into children
                    foreach (var child in multiary.Expressions)
                    {
                        CollectByComponentIndex(child);
                    }
                }
            }
            else if (e is NotExpression notExpr)
            {
                CollectByComponentIndex(notExpr.Expression);
            }
        }

        CollectByComponentIndex(expr);

        // Build result list ordered by component index
        var result = new List<Expression>();
        var sortedIndices = componentsByIndex.Keys.OrderBy(k => k).ToList();

        foreach (var index in sortedIndices)
        {
            var expressions = componentsByIndex[index];
            if (expressions.Count == 1)
            {
                result.Add(expressions[0]);
            }
            else
            {
                // Combine multiple expressions for the same component with AND
                result.Add(Expression.And(expressions.ToArray()));
            }
        }

        return result;
    }

    /// <summary>
    /// Processes _id parameter expressions by querying the Resource table directly.
    /// The _id parameter is a resource-level parameter that matches against Resource.ResourceId.
    /// </summary>
    private async Task<IQueryable<long>> ProcessResourceIdExpressionAsync(
        short? resourceTypeId,
        Expression expr,
        CancellationToken ct)
    {
        _logger.LogDebug("Processing _id parameter expression");

        // The _id parameter is defined as TokenCode field, so we need to extract the resource ID value
        if (expr is StringExpression stringExpr && stringExpr.FieldName == FieldName.TokenCode)
        {
            // Query the Resource table: WHERE ResourceTypeId = ? AND ResourceId = ?
            // When resourceTypeId is null (system-wide search), don't filter by resource type
            var query = _context.Resources
                .Where(r => (!resourceTypeId.HasValue || r.ResourceTypeId == resourceTypeId.Value)
                    && r.ResourceId == stringExpr.Value
                    && !r.IsHistory
                    && !r.IsDeleted)
                .Select(r => r.ResourceSurrogateId);

            return await Task.FromResult(query);
        }

        // Handle other expression types for _id (e.g., MultiaryExpression for multiple IDs)
        if (expr is MultiaryExpression multiaryExpr)
        {
            return await ProcessResourceIdMultiaryExpressionAsync(resourceTypeId, multiaryExpr, ct);
        }

        // Handle NotExpression for _id:not modifier
        if (expr is NotExpression notExpr)
        {
            return await ProcessResourceIdNotExpressionAsync(resourceTypeId, notExpr, ct);
        }

        throw new NotSupportedException($"Expression type {expr.GetType().Name} is not supported for _id parameter query generation");
    }

    /// <summary>
    /// Processes multiary expressions (e.g., multiple _id values) for resource-level parameters.
    /// </summary>
    private Task<IQueryable<long>> ProcessResourceIdMultiaryExpressionAsync(
        short? resourceTypeId,
        MultiaryExpression multiaryExpr,
        CancellationToken ct)
    {
        if (multiaryExpr.Expressions.Count == 0)
        {
            return Task.FromResult(Enumerable.Empty<long>().AsQueryable());
        }

        // CRITICAL FIX: Extract all resource IDs into a List for SQL IN clause generation.
        // Previous implementation created individual queries per ID and chained them with Union/Intersect,
        // which created deeply nested expression trees that caused stack overflow with 100+ IDs.
        // Using .Contains() generates a single flat SQL "WHERE ResourceId IN (...)" clause.
        var resourceIds = new List<string>();
        foreach (var subExpr in multiaryExpr.Expressions)
        {
            if (subExpr is StringExpression stringExpr && stringExpr.FieldName == FieldName.TokenCode)
            {
                resourceIds.Add(stringExpr.Value);
            }
        }

        if (resourceIds.Count == 0)
        {
            return Task.FromResult(Enumerable.Empty<long>().AsQueryable());
        }

        // For OR operations (typical for _id parameter), use Contains() to generate SQL IN clause.
        // This creates a flat expression tree instead of deeply nested Union calls.
        if (multiaryExpr.MultiaryOperation == MultiaryOperator.Or)
        {
            var query = _context.Resources
                .Where(r => (!resourceTypeId.HasValue || r.ResourceTypeId == resourceTypeId.Value)
                    && resourceIds.Contains(r.ResourceId)  // SQL: WHERE ResourceId IN (...)
                    && !r.IsHistory
                    && !r.IsDeleted)
                .Select(r => r.ResourceSurrogateId);

            return Task.FromResult(query);
        }
        else
        {
            // AND operations are rare for _id (semantically unusual to search "_id=A AND _id=B").
            // Fall back to individual queries with Intersect if needed.
            var queries = new List<IQueryable<long>>();
            foreach (var resourceId in resourceIds)
            {
                var query = _context.Resources
                    .Where(r => (!resourceTypeId.HasValue || r.ResourceTypeId == resourceTypeId.Value)
                        && r.ResourceId == resourceId
                        && !r.IsHistory
                        && !r.IsDeleted)
                    .Select(r => r.ResourceSurrogateId);
                queries.Add(query);
            }

            var result = queries[0];
            for (int i = 1; i < queries.Count; i++)
            {
                result = result.Intersect(queries[i]);
            }

            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// Processes _id:not parameter expressions by excluding specified resource IDs.
    /// Gets all resources EXCEPT those with the specified IDs.
    /// </summary>
    private async Task<IQueryable<long>> ProcessResourceIdNotExpressionAsync(
        short? resourceTypeId,
        NotExpression notExpr,
        CancellationToken ct)
    {
        _logger.LogDebug("Processing _id:not parameter expression");

        // Process the inner expression to get matching resource IDs
        var innerMatchingIds = await ProcessResourceIdExpressionAsync(resourceTypeId, notExpr.Expression, ct);

        // Get all resources (non-history, non-deleted)
        // When resourceTypeId is specified, only consider that type; otherwise all types
        var allResourceIds = _context.Resources
            .Where(r => (!resourceTypeId.HasValue || r.ResourceTypeId == resourceTypeId.Value) && !r.IsHistory && !r.IsDeleted)
            .Select(r => r.ResourceSurrogateId);

        // Use EXCEPT to get all resources NOT in the inner matching set
        return allResourceIds.Except(innerMatchingIds);
    }

    /// <summary>
    /// Processes _lastUpdated parameter expressions by querying the Resource table directly.
    /// The _lastUpdated parameter is stored as resourceSurrogateId, which encodes the DateTime via IdHelper.ToId().
    /// Compares resourceSurrogateId directly against the value produced by IdHelper.ToId() for the target DateTime.
    /// </summary>
    private async Task<IQueryable<long>> ProcessResourceLastUpdatedExpressionAsync(
        short? resourceTypeId,
        Expression expr,
        CancellationToken ct)
    {
        _logger.LogDebug("Processing _lastUpdated parameter expression");

        // The _lastUpdated parameter uses BinaryExpression for comparison operators (>, >=, <, <=, =, !=)
        if (expr is BinaryExpression binaryExpr && binaryExpr.Value is DateTimeOffset dateTimeValue)
        {
            // Query the Resource table: WHERE ResourceTypeId = ? AND resourceSurrogateId [operator] ToId(dateTime)
            var targetId = dateTimeValue.ToId();

            // When resourceTypeId is null (system-wide search), don't filter by resource type
            var query = binaryExpr.BinaryOperator switch
            {
                BinaryOperator.Equal =>
                    _context.Resources
                        .Where(r => (!resourceTypeId.HasValue || r.ResourceTypeId == resourceTypeId.Value)
                            && r.ResourceSurrogateId == targetId
                            && !r.IsHistory
                            && !r.IsDeleted),
                BinaryOperator.GreaterThan =>
                    _context.Resources
                        .Where(r => (!resourceTypeId.HasValue || r.ResourceTypeId == resourceTypeId.Value)
                            && r.ResourceSurrogateId > targetId
                            && !r.IsHistory
                            && !r.IsDeleted),
                BinaryOperator.GreaterThanOrEqual =>
                    _context.Resources
                        .Where(r => (!resourceTypeId.HasValue || r.ResourceTypeId == resourceTypeId.Value)
                            && r.ResourceSurrogateId >= targetId
                            && !r.IsHistory
                            && !r.IsDeleted),
                BinaryOperator.LessThan =>
                    _context.Resources
                        .Where(r => (!resourceTypeId.HasValue || r.ResourceTypeId == resourceTypeId.Value)
                            && r.ResourceSurrogateId < targetId
                            && !r.IsHistory
                            && !r.IsDeleted),
                BinaryOperator.LessThanOrEqual =>
                    _context.Resources
                        .Where(r => (!resourceTypeId.HasValue || r.ResourceTypeId == resourceTypeId.Value)
                            && r.ResourceSurrogateId <= targetId
                            && !r.IsHistory
                            && !r.IsDeleted),
                BinaryOperator.NotEqual =>
                    _context.Resources
                        .Where(r => (!resourceTypeId.HasValue || r.ResourceTypeId == resourceTypeId.Value)
                            && r.ResourceSurrogateId != targetId
                            && !r.IsHistory
                            && !r.IsDeleted),
                _ => throw new NotSupportedException($"Binary operator {binaryExpr.BinaryOperator} is not supported for _lastUpdated")
            };

            return await Task.FromResult(query.Select(r => r.ResourceSurrogateId));
        }

        // Handle other expression types for _lastUpdated (e.g., MultiaryExpression for multiple dates with OR/AND)
        if (expr is MultiaryExpression multiaryExpr)
        {
            return await ProcessResourceLastUpdatedMultiaryExpressionAsync(resourceTypeId, multiaryExpr, ct);
        }

        throw new NotSupportedException($"Expression type {expr.GetType().Name} is not supported for _lastUpdated parameter query generation");
    }

    /// <summary>
    /// Processes multiary expressions (e.g., multiple _lastUpdated constraints) for resource-level parameters.
    /// </summary>
    private Task<IQueryable<long>> ProcessResourceLastUpdatedMultiaryExpressionAsync(
        short? resourceTypeId,
        MultiaryExpression multiaryExpr,
        CancellationToken ct)
    {
        if (multiaryExpr.Expressions.Count == 0)
        {
            return Task.FromResult(Enumerable.Empty<long>().AsQueryable());
        }

        var queries = new List<IQueryable<long>>();
        foreach (var subExpr in multiaryExpr.Expressions)
        {
            if (subExpr is BinaryExpression binaryExpr && binaryExpr.Value is DateTimeOffset dateTimeValue)
            {
                var targetId = dateTimeValue.ToId();

                // When resourceTypeId is null (system-wide search), don't filter by resource type
                var query = binaryExpr.BinaryOperator switch
                {
                    BinaryOperator.Equal =>
                        _context.Resources
                            .Where(r => (!resourceTypeId.HasValue || r.ResourceTypeId == resourceTypeId.Value)
                                && r.ResourceSurrogateId == targetId
                                && !r.IsHistory
                                && !r.IsDeleted),
                    BinaryOperator.GreaterThan =>
                        _context.Resources
                            .Where(r => (!resourceTypeId.HasValue || r.ResourceTypeId == resourceTypeId.Value)
                                && r.ResourceSurrogateId > targetId
                                && !r.IsHistory
                                && !r.IsDeleted),
                    BinaryOperator.GreaterThanOrEqual =>
                        _context.Resources
                            .Where(r => (!resourceTypeId.HasValue || r.ResourceTypeId == resourceTypeId.Value)
                                && r.ResourceSurrogateId >= targetId
                                && !r.IsHistory
                                && !r.IsDeleted),
                    BinaryOperator.LessThan =>
                        _context.Resources
                            .Where(r => (!resourceTypeId.HasValue || r.ResourceTypeId == resourceTypeId.Value)
                                && r.ResourceSurrogateId < targetId
                                && !r.IsHistory
                                && !r.IsDeleted),
                    BinaryOperator.LessThanOrEqual =>
                        _context.Resources
                            .Where(r => (!resourceTypeId.HasValue || r.ResourceTypeId == resourceTypeId.Value)
                                && r.ResourceSurrogateId <= targetId
                                && !r.IsHistory
                                && !r.IsDeleted),
                    BinaryOperator.NotEqual =>
                        _context.Resources
                            .Where(r => (!resourceTypeId.HasValue || r.ResourceTypeId == resourceTypeId.Value)
                                && r.ResourceSurrogateId != targetId
                                && !r.IsHistory
                                && !r.IsDeleted),
                    _ => throw new NotSupportedException($"Binary operator {binaryExpr.BinaryOperator} is not supported for _lastUpdated")
                };

                queries.Add(query.Select(r => r.ResourceSurrogateId));
            }
        }

        if (queries.Count == 0)
        {
            return Task.FromResult(Enumerable.Empty<long>().AsQueryable());
        }

        // Combine based on operator
        var result = queries[0];
        for (int i = 1; i < queries.Count; i++)
        {
            result = multiaryExpr.MultiaryOperation == MultiaryOperator.Or
                ? result.Union(queries[i])
                : result.Intersect(queries[i]);
        }

        return Task.FromResult(result);
    }

    /// <summary>
    /// Processes _type parameter expressions by querying the Resource table directly.
    /// The _type parameter is a resource-level parameter that matches against Resource.ResourceTypeId.
    /// Uses the SearchIndexReferenceDataCache to look up resource type IDs.
    /// </summary>
    private async Task<IQueryable<long>> ProcessResourceTypeExpressionAsync(
        short? resourceTypeId,
        Expression expr,
        CancellationToken ct)
    {
        _logger.LogDebug("Processing _type parameter expression");

        // For _type parameter, extract resource type names from the expression
        // Since _type is defined as Token type with no expression, we need to handle InExpression<string>
        if (expr is InExpression<string> inExpr)
        {
            return await ProcessInTokenCodeExpressionAsync(resourceTypeId, inExpr, ct);
        }

        // Handle single value StringExpression
        if (expr is StringExpression stringExpr && stringExpr.FieldName == FieldName.TokenCode)
        {
            var typeId = await _cache.GetResourceTypeIdAsync(stringExpr.Value);

            if (typeId.HasValue)
            {
                var query = _context.Resources
                    .Where(r => r.ResourceTypeId == typeId.Value
                        && !r.IsHistory
                        && !r.IsDeleted)
                    .Select(r => r.ResourceSurrogateId);
                return await Task.FromResult(query);
            }

            return Enumerable.Empty<long>().AsQueryable();
        }

        // Handle MultiaryExpression for multiple resource types with OR/AND
        if (expr is MultiaryExpression multiaryExpr)
        {
            return await ProcessResourceTypeMultiaryExpressionAsync(multiaryExpr, ct);
        }

        // Handle NotExpression for _type:not modifier
        if (expr is NotExpression notExpr)
        {
            return await ProcessResourceTypeNotExpressionAsync(resourceTypeId, notExpr, ct);
        }

        throw new NotSupportedException($"Expression type {expr.GetType().Name} is not supported for _type parameter query generation");
    }

    /// <summary>
    /// Processes multiary expressions for _type parameter (multiple resource types with OR/AND).
    /// Uses the SearchIndexReferenceDataCache to look up resource type IDs.
    /// </summary>
    private async Task<IQueryable<long>> ProcessResourceTypeMultiaryExpressionAsync(
        MultiaryExpression multiaryExpr,
        CancellationToken ct)
    {
        if (multiaryExpr.Expressions.Count == 0)
        {
            return Enumerable.Empty<long>().AsQueryable();
        }

        var resourceTypeIds = new List<short>();

        foreach (var subExpr in multiaryExpr.Expressions)
        {
            if (subExpr is StringExpression stringExpr && stringExpr.FieldName == FieldName.TokenCode)
            {
                var typeId = await _cache.GetResourceTypeIdAsync(stringExpr.Value);
                if (typeId.HasValue)
                {
                    resourceTypeIds.Add(typeId.Value);
                }
            }
        }

        if (resourceTypeIds.Count == 0)
        {
            return Enumerable.Empty<long>().AsQueryable();
        }

        // Return query: WHERE ResourceTypeId IN (...)
        return _context.Resources
            .Where(r => resourceTypeIds.Contains(r.ResourceTypeId) && !r.IsHistory && !r.IsDeleted)
            .Select(r => r.ResourceSurrogateId);
    }

    /// <summary>
    /// Processes _type:not parameter expressions by excluding specified resource types.
    /// Gets all resources EXCEPT those with the specified resource types.
    /// </summary>
    private async Task<IQueryable<long>> ProcessResourceTypeNotExpressionAsync(
        short? resourceTypeId,
        NotExpression notExpr,
        CancellationToken ct)
    {
        _logger.LogDebug("Processing _type:not parameter expression");

        // Process the inner expression to get matching resource type IDs
        var innerMatchingIds = await ProcessResourceTypeExpressionAsync(resourceTypeId, notExpr.Expression, ct);

        // Get all resources (non-history, non-deleted)
        // When resourceTypeId is specified, only consider that type; otherwise all types
        var allResourceIds = _context.Resources
            .Where(r => (!resourceTypeId.HasValue || r.ResourceTypeId == resourceTypeId.Value) && !r.IsHistory && !r.IsDeleted)
            .Select(r => r.ResourceSurrogateId);

        // Use EXCEPT to get all resources NOT in the inner matching set
        return allResourceIds.Except(innerMatchingIds);
    }

    private async Task<IQueryable<long>> ProcessExpressionAsync(
        short? resourceTypeId,
        short? searchParamId,
        Expression expr,
        CancellationToken ct)
    {
        if (expr is MultiaryExpression multiaryExpr)
        {
            return await ProcessMultiaryExpressionAsync(resourceTypeId, searchParamId, multiaryExpr, ct);
        }

        if (expr is StringExpression stringExpr)
        {
            return await ProcessStringExpressionAsync(resourceTypeId, searchParamId, stringExpr, ct);
        }

        if (expr is BinaryExpression binaryExpr)
        {
            return await ProcessBinaryExpressionAsync(resourceTypeId, searchParamId, binaryExpr, ct);
        }

        if (expr is NotExpression notExpr)
        {
            return await ProcessNotExpressionAsync(resourceTypeId, searchParamId, notExpr, ct);
        }

        // Handle InExpression<T> using reflection to get the generic type parameter
        var exprType = expr.GetType();
        if (exprType.IsGenericType && exprType.GetGenericTypeDefinition() == typeof(InExpression<>))
        {
            var genericArg = exprType.GetGenericArguments()[0];
            var fieldNameProperty = exprType.GetProperty(nameof(IFieldExpression.FieldName));
            var fieldName = (FieldName)fieldNameProperty!.GetValue(expr)!;

            if (fieldName == FieldName.TokenCode && genericArg == typeof(string))
            {
                return await ProcessInTokenCodeExpressionAsync(resourceTypeId, expr as InExpression<string> ?? throw new InvalidCastException(), ct);
            }
        }

        throw new NotSupportedException($"Expression type {expr.GetType().Name} is not supported for query generation");
    }

    private async Task<IQueryable<long>> ProcessInTokenCodeExpressionAsync(
        short? resourceTypeId,
        InExpression<string> expression,
        CancellationToken ct)
    {
        // For _type parameter: convert resource type names to IDs and query Resource table
        var resourceTypeIds = new List<short>();

        foreach (var resourceTypeName in expression.Values)
        {
            // Try to get the resource type ID for this name using cache (consistent with ProcessResourceTypeMultiaryExpressionAsync)
            var typeId = await _cache.GetResourceTypeIdAsync(resourceTypeName);

            if (typeId.HasValue)
            {
                resourceTypeIds.Add(typeId.Value);
            }
        }

        if (resourceTypeIds.Count == 0)
        {
            return Enumerable.Empty<long>().AsQueryable();
        }

        // Return query: WHERE ResourceTypeId IN (...)
        return _context.Resources
            .Where(r => resourceTypeIds.Contains(r.ResourceTypeId) && !r.IsHistory && !r.IsDeleted)
            .Select(r => r.ResourceSurrogateId);
    }

    private async Task<IQueryable<long>> ProcessMultiaryExpressionAsync(
        short? resourceTypeId,
        short? searchParamId,
        MultiaryExpression multiaryExpr,
        CancellationToken ct)
    {
        if (multiaryExpr.Expressions.Count == 0)
        {
            return Enumerable.Empty<long>().AsQueryable();
        }

        _logger.LogDebug(
            "Processing MultiaryExpression: Operation={Operation}, ExpressionCount={Count}, ResourceTypeId={ResourceTypeId}, SearchParamId={SearchParamId}",
            multiaryExpr.MultiaryOperation,
            multiaryExpr.Expressions.Count,
            resourceTypeId,
            searchParamId);

        // Special handling for DateTime OR expressions - generate single query with compound WHERE
        // instead of using UNION which can have performance and correctness issues in EF Core
        if (multiaryExpr.MultiaryOperation == MultiaryOperator.Or &&
            multiaryExpr.Expressions.All(e => e is BinaryExpression be &&
                (be.FieldName == FieldName.DateTimeStart || be.FieldName == FieldName.DateTimeEnd)))
        {
            return GenerateDateTimeOrQuery(resourceTypeId, searchParamId, multiaryExpr);
        }

        // Special handling for Quantity AND expressions - all filters must apply to the SAME row
        // Without this, each condition (system, code, value) would match independently and incorrectly combine
        if (multiaryExpr.MultiaryOperation == MultiaryOperator.And &&
            IsQuantityAndExpression(multiaryExpr))
        {
            return await GenerateQuantityAndQueryAsync(resourceTypeId, searchParamId, multiaryExpr, ct);
        }

        // Special handling for Token AND expressions - all filters (system, code, missing system) must apply to the SAME row
        // This handles patterns like "|code" which generates: And(Missing(TokenSystem), StringEquals(TokenCode, "code"))
        if (multiaryExpr.MultiaryOperation == MultiaryOperator.And &&
            IsTokenAndExpression(multiaryExpr))
        {
            return await GenerateTokenAndQueryAsync(resourceTypeId, searchParamId, multiaryExpr, ct);
        }

        // Process each sub-expression
        var queries = new List<IQueryable<long>>();
        for (int idx = 0; idx < multiaryExpr.Expressions.Count; idx++)
        {
            var subExpr = multiaryExpr.Expressions[idx];
            _logger.LogDebug("Processing sub-expression {Index}: {ExprType}", idx, subExpr.GetType().Name);
            var query = await ProcessExpressionAsync(resourceTypeId, searchParamId, subExpr, ct);
            queries.Add(query);
        }

        // Combine based on operator (OR for values, AND would be unusual here)
        var result = queries[0];
        for (int i = 1; i < queries.Count; i++)
        {
            result = multiaryExpr.MultiaryOperation == MultiaryOperator.Or
                ? result.Union(queries[i])
                : result.Intersect(queries[i]);
        }

        return result;
    }

    /// <summary>
    /// Generates a single DateTime query with compound OR conditions in the WHERE clause.
    /// This is more efficient than UNION and avoids potential EF Core UNION issues.
    /// Uses explicit inline comparisons that EF Core can translate to SQL.
    /// </summary>
    private IQueryable<long> GenerateDateTimeOrQuery(
        short? resourceTypeId,
        short? searchParamId,
        MultiaryExpression multiaryExpr)
    {
        _logger.LogDebug("Using optimized DateTime OR query generation");

        // Start with base query
        var query = _context.DateTimeSearchParams
            .Where(sp => (!resourceTypeId.HasValue || sp.ResourceTypeId == resourceTypeId.Value)
                && (!searchParamId.HasValue || sp.SearchParamId == searchParamId.Value));

        // Extract conditions from binary expressions
        var conditions = new List<(FieldName Field, BinaryOperator Op, DateTime Value)>();

        foreach (var expr in multiaryExpr.Expressions)
        {
            if (expr is BinaryExpression binaryExpr)
            {
                DateTime value = binaryExpr.Value switch
                {
                    DateTime dt => dt,
                    DateTimeOffset dto => dto.UtcDateTime,
                    _ => Convert.ToDateTime(binaryExpr.Value)
                };

                conditions.Add((binaryExpr.FieldName, binaryExpr.BinaryOperator, value));

                _logger.LogDebug(
                    "DateTime OR condition: Field={Field}, Op={Op}, Value={Value}",
                    binaryExpr.FieldName,
                    binaryExpr.BinaryOperator,
                    value.ToString("o"));
            }
        }

        // Handle the common case of 2 conditions (e.g., ne search: StartDateTime < X OR EndDateTime > Y)
        if (conditions.Count == 2)
        {
            var c1 = conditions[0];
            var c2 = conditions[1];

            // Must use explicit comparisons for EF Core to translate to SQL
            // Build the WHERE clause based on the specific conditions
            return BuildTwoConditionDateTimeOrQuery(query, c1, c2);
        }

        // For single condition
        if (conditions.Count == 1)
        {
            var c = conditions[0];
            return BuildSingleConditionDateTimeQuery(query, c);
        }

        // For 3+ conditions, fall back to UNION (less common case)
        _logger.LogWarning("DateTime OR with {Count} conditions - using UNION fallback", conditions.Count);
        IQueryable<long>? result = null;
        foreach (var c in conditions)
        {
            var conditionQuery = BuildSingleConditionDateTimeQuery(query, c);
            result = result == null ? conditionQuery : result.Union(conditionQuery);
        }
        return result ?? Enumerable.Empty<long>().AsQueryable();
    }

    /// <summary>
    /// Builds a DateTime query with a single condition that EF Core can translate to SQL.
    /// </summary>
    private static IQueryable<long> BuildSingleConditionDateTimeQuery(
        IQueryable<Entities.DateTimeSearchParamEntity> baseQuery,
        (FieldName Field, BinaryOperator Op, DateTime Value) condition)
    {
        var (field, op, value) = condition;

        // Use explicit conditions based on field and operator to ensure EF Core translation
        if (field == FieldName.DateTimeStart)
        {
            return op switch
            {
                BinaryOperator.Equal => baseQuery.Where(sp => sp.StartDateTime == value).Select(sp => sp.ResourceSurrogateId),
                BinaryOperator.NotEqual => baseQuery.Where(sp => sp.StartDateTime != value).Select(sp => sp.ResourceSurrogateId),
                BinaryOperator.GreaterThan => baseQuery.Where(sp => sp.StartDateTime > value).Select(sp => sp.ResourceSurrogateId),
                BinaryOperator.GreaterThanOrEqual => baseQuery.Where(sp => sp.StartDateTime >= value).Select(sp => sp.ResourceSurrogateId),
                BinaryOperator.LessThan => baseQuery.Where(sp => sp.StartDateTime < value).Select(sp => sp.ResourceSurrogateId),
                BinaryOperator.LessThanOrEqual => baseQuery.Where(sp => sp.StartDateTime <= value).Select(sp => sp.ResourceSurrogateId),
                _ => Enumerable.Empty<long>().AsQueryable()
            };
        }
        else // DateTimeEnd
        {
            return op switch
            {
                BinaryOperator.Equal => baseQuery.Where(sp => sp.EndDateTime == value).Select(sp => sp.ResourceSurrogateId),
                BinaryOperator.NotEqual => baseQuery.Where(sp => sp.EndDateTime != value).Select(sp => sp.ResourceSurrogateId),
                BinaryOperator.GreaterThan => baseQuery.Where(sp => sp.EndDateTime > value).Select(sp => sp.ResourceSurrogateId),
                BinaryOperator.GreaterThanOrEqual => baseQuery.Where(sp => sp.EndDateTime >= value).Select(sp => sp.ResourceSurrogateId),
                BinaryOperator.LessThan => baseQuery.Where(sp => sp.EndDateTime < value).Select(sp => sp.ResourceSurrogateId),
                BinaryOperator.LessThanOrEqual => baseQuery.Where(sp => sp.EndDateTime <= value).Select(sp => sp.ResourceSurrogateId),
                _ => Enumerable.Empty<long>().AsQueryable()
            };
        }
    }

    /// <summary>
    /// Builds a DateTime query with two OR conditions that EF Core can translate to SQL.
    /// Handles all common cases like ne (not equal) search which uses StartDateTime &lt; X OR EndDateTime > Y.
    /// </summary>
    private static IQueryable<long> BuildTwoConditionDateTimeOrQuery(
        IQueryable<Entities.DateTimeSearchParamEntity> baseQuery,
        (FieldName Field, BinaryOperator Op, DateTime Value) c1,
        (FieldName Field, BinaryOperator Op, DateTime Value) c2)
    {
        // Generate the specific WHERE clause based on the conditions
        // This covers the common cases for date search comparators

        // Case: StartDateTime LessThan AND EndDateTime GreaterThan (ne search)
        if (c1.Field == FieldName.DateTimeStart && c1.Op == BinaryOperator.LessThan &&
            c2.Field == FieldName.DateTimeEnd && c2.Op == BinaryOperator.GreaterThan)
        {
            return baseQuery.Where(sp => sp.StartDateTime < c1.Value || sp.EndDateTime > c2.Value)
                .Select(sp => sp.ResourceSurrogateId);
        }

        // Case: EndDateTime GreaterThan AND StartDateTime LessThan (ne search, reversed order)
        if (c1.Field == FieldName.DateTimeEnd && c1.Op == BinaryOperator.GreaterThan &&
            c2.Field == FieldName.DateTimeStart && c2.Op == BinaryOperator.LessThan)
        {
            return baseQuery.Where(sp => sp.EndDateTime > c1.Value || sp.StartDateTime < c2.Value)
                .Select(sp => sp.ResourceSurrogateId);
        }

        // Case: StartDateTime GreaterThanOrEqual AND EndDateTime LessThanOrEqual (eq search - though this is AND, not OR)
        // This shouldn't happen for OR, but handle it just in case
        if (c1.Field == FieldName.DateTimeStart && c1.Op == BinaryOperator.GreaterThanOrEqual &&
            c2.Field == FieldName.DateTimeEnd && c2.Op == BinaryOperator.LessThanOrEqual)
        {
            return baseQuery.Where(sp => sp.StartDateTime >= c1.Value || sp.EndDateTime <= c2.Value)
                .Select(sp => sp.ResourceSurrogateId);
        }

        // Generic fallback: use UNION for any other combination
        var q1 = BuildSingleConditionDateTimeQuery(baseQuery, c1);
        var q2 = BuildSingleConditionDateTimeQuery(baseQuery, c2);
        return q1.Union(q2);
    }

    /// <summary>
    /// Checks if a MultiaryExpression contains Quantity field expressions (system, code, value).
    /// </summary>
    private static bool IsQuantityAndExpression(MultiaryExpression multiaryExpr)
    {
        // A Quantity AND expression will contain some combination of:
        // - StringExpression with FieldName.QuantitySystem
        // - StringExpression with FieldName.QuantityCode
        // - BinaryExpression with FieldName.Quantity
        return multiaryExpr.Expressions.Any(e =>
            (e is StringExpression se && (se.FieldName == FieldName.QuantitySystem || se.FieldName == FieldName.QuantityCode)) ||
            (e is BinaryExpression be && be.FieldName == FieldName.Quantity));
    }

    /// <summary>
    /// Generates a single Quantity query with all filters (system, code, value) applied to the SAME row.
    /// This is essential for correctness - each condition must match the same indexed quantity value.
    /// </summary>
    private async Task<IQueryable<long>> GenerateQuantityAndQueryAsync(
        short? resourceTypeId,
        short? searchParamId,
        MultiaryExpression multiaryExpr,
        CancellationToken ct)
    {
        _logger.LogDebug("Using optimized Quantity AND query generation");

        // Extract the components from the AND expression
        string? systemUri = null;
        string? code = null;
        BinaryExpression? valueExpr = null;

        foreach (var expr in multiaryExpr.Expressions)
        {
            if (expr is StringExpression stringExpr)
            {
                switch (stringExpr.FieldName)
                {
                    case FieldName.QuantitySystem:
                        systemUri = stringExpr.Value;
                        break;
                    case FieldName.QuantityCode:
                        code = stringExpr.Value;
                        break;
                }
            }
            else if (expr is BinaryExpression binaryExpr && binaryExpr.FieldName == FieldName.Quantity)
            {
                valueExpr = binaryExpr;
            }
        }

        // Build base query with resource type and search param filters
        var query = _context.QuantitySearchParams
            .Where(sp => (!resourceTypeId.HasValue || sp.ResourceTypeId == resourceTypeId.Value)
                && (!searchParamId.HasValue || sp.SearchParamId == searchParamId.Value));

        // Add system filter if specified
        if (!string.IsNullOrEmpty(systemUri))
        {
            var systemId = await _cache.GetOrCreateSystemIdAsync(systemUri);
            if (!systemId.HasValue)
            {
                _logger.LogDebug("Quantity system not found: {SystemUri}", systemUri);
                return Enumerable.Empty<long>().AsQueryable();
            }
            query = query.Where(sp => sp.SystemId == systemId.Value);
        }

        // Add code filter if specified
        if (!string.IsNullOrEmpty(code))
        {
            var quantityCodeId = await _cache.GetOrCreateQuantityCodeIdAsync(code);
            if (!quantityCodeId.HasValue)
            {
                _logger.LogDebug("Quantity code not found: {Code}", code);
                return Enumerable.Empty<long>().AsQueryable();
            }
            query = query.Where(sp => sp.QuantityCodeId == quantityCodeId.Value);
        }

        // Add value comparison if specified
        if (valueExpr != null)
        {
            var value = Convert.ToDecimal(valueExpr.Value);

            _logger.LogDebug(
                "Quantity AND query: System={System}, Code={Code}, Value={Value}, Op={Op}",
                systemUri,
                code,
                value,
                valueExpr.BinaryOperator);

            query = valueExpr.BinaryOperator switch
            {
                BinaryOperator.Equal => query.Where(sp => sp.LowValue <= value && sp.HighValue >= value),
                BinaryOperator.GreaterThan => query.Where(sp => sp.LowValue > value),
                BinaryOperator.GreaterThanOrEqual => query.Where(sp => sp.LowValue >= value),
                BinaryOperator.LessThan => query.Where(sp => sp.HighValue < value),
                BinaryOperator.LessThanOrEqual => query.Where(sp => sp.HighValue <= value),
                BinaryOperator.NotEqual => query.Where(sp => sp.HighValue < value || sp.LowValue > value),
                _ => throw new NotSupportedException($"BinaryOperator {valueExpr.BinaryOperator} is not supported for Quantity")
            };
        }

        return query.Select(sp => sp.ResourceSurrogateId);
    }

    /// <summary>
    /// Checks if a MultiaryExpression contains Token field expressions (system, code, missing system).
    /// </summary>
    private static bool IsTokenAndExpression(MultiaryExpression multiaryExpr)
    {
        // A Token AND expression will contain some combination of:
        // - StringExpression with FieldName.TokenSystem
        // - StringExpression with FieldName.TokenCode
        // - MissingFieldExpression with FieldName.TokenSystem (for |code pattern)
        return multiaryExpr.Expressions.Any(e =>
            (e is StringExpression se && (se.FieldName == FieldName.TokenSystem || se.FieldName == FieldName.TokenCode)) ||
            (e is MissingFieldExpression mfe && mfe.FieldName == FieldName.TokenSystem));
    }

    /// <summary>
    /// Generates a single Token query with all filters (system, code, missing system) applied to the SAME row.
    /// This is essential for correctness - each condition must match the same indexed token value.
    /// Handles patterns like:
    /// - "|code" (empty system): MissingFieldExpression(TokenSystem) AND StringExpression(TokenCode)
    /// - "system|code" (full token): StringExpression(TokenSystem) AND StringExpression(TokenCode)
    /// </summary>
    private async Task<IQueryable<long>> GenerateTokenAndQueryAsync(
        short? resourceTypeId,
        short? searchParamId,
        MultiaryExpression multiaryExpr,
        CancellationToken ct)
    {
        _logger.LogDebug("Using optimized Token AND query generation");

        // Extract the components from the AND expression
        string? system = null;
        string? code = null;
        bool requireMissingSystem = false;

        foreach (var expr in multiaryExpr.Expressions)
        {
            if (expr is StringExpression stringExpr)
            {
                switch (stringExpr.FieldName)
                {
                    case FieldName.TokenSystem:
                        system = stringExpr.Value;
                        break;
                    case FieldName.TokenCode:
                        code = stringExpr.Value;
                        break;
                }
            }
            else if (expr is MissingFieldExpression missingExpr && missingExpr.FieldName == FieldName.TokenSystem)
            {
                // |code pattern: system must be null/missing
                requireMissingSystem = true;
            }
        }

        // Build base query with resource type and search param filters
        var query = _context.TokenSearchParams
            .Where(sp => (!resourceTypeId.HasValue || sp.ResourceTypeId == resourceTypeId.Value)
                && (!searchParamId.HasValue || sp.SearchParamId == searchParamId.Value));

        // Add system filter if specified
        if (!string.IsNullOrEmpty(system))
        {
            var systemId = await _cache.GetOrCreateSystemIdAsync(system);
            if (!systemId.HasValue)
            {
                _logger.LogDebug("Token system not found: {System}", system);
                return Enumerable.Empty<long>().AsQueryable();
            }
            query = query.Where(sp => sp.SystemId == systemId.Value);
        }
        else if (requireMissingSystem)
        {
            // |code pattern: require that SystemId is null
            query = query.Where(sp => sp.SystemId == null);
        }

        // Add code filter if specified
        // FHIR Spec: Token searches are case-insensitive for the code portion.
        // Use case-insensitive collation since the Code column uses Latin1_General_100_CS_AS (case-sensitive).
        if (!string.IsNullOrEmpty(code))
        {
            query = query.Where(sp => EF.Functions.Collate(sp.Code, "Latin1_General_100_CI_AS") == code);
        }

        _logger.LogDebug(
            "Token AND query: System={System}, Code={Code}, RequireMissingSystem={RequireMissingSystem}",
            system,
            code,
            requireMissingSystem);

        return query.Select(sp => sp.ResourceSurrogateId);
    }

    private async Task<IQueryable<long>> ProcessStringExpressionAsync(
        short? resourceTypeId,
        short? searchParamId,
        StringExpression stringExpr,
        CancellationToken ct)
    {
        // Determine which table to query based on FieldName
        return stringExpr.FieldName switch
        {
            FieldName.String => await GenerateStringQueryAsync(resourceTypeId, searchParamId, stringExpr.Value, stringExpr.StringOperator, stringExpr.IgnoreCase, ct),
            FieldName.Uri => GenerateUriQuery(resourceTypeId, searchParamId, stringExpr.Value, stringExpr.StringOperator),
            FieldName.TokenCode => await GenerateTokenQueryAsync(resourceTypeId, searchParamId, null, stringExpr.Value, ct),
            FieldName.TokenSystem => await GenerateTokenQueryAsync(resourceTypeId, searchParamId, stringExpr.Value, null, ct),
            FieldName.TokenText => GenerateTokenTextQuery(resourceTypeId, searchParamId, stringExpr.Value, stringExpr.StringOperator),
            FieldName.ReferenceResourceId => await GenerateReferenceQueryByIdAsync(resourceTypeId, searchParamId, stringExpr.Value, ct),
            FieldName.ReferenceResourceType => await GenerateReferenceQueryByTypeAsync(resourceTypeId, searchParamId, stringExpr.Value, ct),
            FieldName.QuantitySystem => await GenerateQuantitySystemQueryAsync(resourceTypeId, searchParamId, stringExpr.Value, ct),
            FieldName.QuantityCode => await GenerateQuantityCodeQueryAsync(resourceTypeId, searchParamId, stringExpr.Value, ct),
            _ => throw new NotSupportedException($"StringExpression with FieldName {stringExpr.FieldName} is not supported")
        };
    }

    private async Task<IQueryable<long>> ProcessBinaryExpressionAsync(
        short? resourceTypeId,
        short? searchParamId,
        BinaryExpression binaryExpr,
        CancellationToken ct)
    {
        // Determine which table to query based on FieldName
        return binaryExpr.FieldName switch
        {
            FieldName.Number => GenerateNumberQuery(resourceTypeId, searchParamId, binaryExpr),
            FieldName.DateTimeStart or FieldName.DateTimeEnd => GenerateDateTimeQuery(resourceTypeId, searchParamId, binaryExpr),
            FieldName.Quantity => await GenerateQuantityQueryAsync(resourceTypeId, searchParamId, binaryExpr, ct),
            _ => throw new NotSupportedException($"BinaryExpression with FieldName {binaryExpr.FieldName} is not supported")
        };
    }

    private async Task<IQueryable<long>> ProcessNotExpressionAsync(
        short? resourceTypeId,
        short? searchParamId,
        NotExpression notExpr,
        CancellationToken ct)
    {
        _logger.LogDebug("Processing NOT expression");

        // Process the inner expression to get matching resource IDs
        var innerMatchingIds = await ProcessExpressionAsync(resourceTypeId, searchParamId, notExpr.Expression, ct);

        // Get all resources of this type (non-history, non-deleted)
        // When resourceTypeId is null (system-wide search), don't filter by resource type
        var allResourceIds = _context.Resources
            .Where(r => (!resourceTypeId.HasValue || r.ResourceTypeId == resourceTypeId.Value) && !r.IsHistory && !r.IsDeleted)
            .Select(r => r.ResourceSurrogateId);

        // Use EXCEPT instead of WHERE NOT IN to avoid deeply nested expression trees
        // that can cause stack overflow in EF Core's ExpressionTreeFuncletizer.
        // EXCEPT generates cleaner SQL: SELECT ... EXCEPT SELECT ...
        // instead of WHERE NOT EXISTS (complex nested subquery)
        return allResourceIds.Except(innerMatchingIds);
    }

    private async Task<IQueryable<long>> GenerateStringQueryAsync(
        short? resourceTypeId,
        short? searchParamId,
        string searchText,
        StringOperator stringOperator,
        bool ignoreCase,
        CancellationToken ct)
    {
        // Base query filters by resource type and search param
        var baseQuery = _context.StringSearchParams
            .Where(sp => (!resourceTypeId.HasValue || sp.ResourceTypeId == resourceTypeId.Value)
                && (!searchParamId.HasValue || sp.SearchParamId == searchParamId.Value));

        // FHIR String Search Rules:
        // - No modifier (starts-with): case-insensitive, accent-insensitive
        // - :exact: case-sensitive, accent-sensitive (must match exactly)
        // - :contains: case-insensitive, accent-insensitive
        //
        // Implementation approach:
        // - Text column stores ORIGINAL case text (first 256 chars)
        // - TextOverflow stores remaining chars for strings > 256 chars
        // - Query-time collation handles case-sensitivity:
        //   - Case-insensitive: Latin1_General_100_CI_AI
        //   - Case-sensitive (:exact): Latin1_General_100_CS_AS

        IQueryable<long> query;

        // Collation to use based on case-sensitivity requirement
        // CI_AI = Case-Insensitive, Accent-Insensitive (FHIR default for string search)
        // CS_AS = Case-Sensitive, Accent-Sensitive (FHIR :exact modifier)
        var collation = ignoreCase ? "Latin1_General_100_CI_AI" : "Latin1_General_100_CS_AS";

        // For long strings (those with TextOverflow), we need to search the combined text
        // Text contains first 256 chars, TextOverflow contains the rest
        switch (stringOperator)
        {
            case StringOperator.StartsWith:
                {
                    // Build starts-with pattern
                    var pattern = $"{searchText}%";

                    if (searchText.Length > 256)
                    {
                        // Search value is longer than 256 chars - need to match against concatenated text
                        query = baseQuery
                            .Where(sp => sp.TextOverflow != null &&
                                EF.Functions.Like(
                                    EF.Functions.Collate(sp.Text + sp.TextOverflow, collation),
                                    pattern))
                            .Select(sp => sp.ResourceSurrogateId);
                    }
                    else
                    {
                        // Short search value - Text column is sufficient for starts-with
                        query = baseQuery
                            .Where(sp => EF.Functions.Like(
                                EF.Functions.Collate(sp.Text, collation),
                                pattern))
                            .Select(sp => sp.ResourceSurrogateId);
                    }
                    break;
                }

            case StringOperator.Contains:
                {
                    // Build contains pattern
                    var pattern = $"%{searchText}%";

                    // For contains, we need to search the full text including overflow
                    query = baseQuery
                        .Where(sp => EF.Functions.Like(
                            EF.Functions.Collate(
                                sp.TextOverflow != null ? sp.Text + sp.TextOverflow : sp.Text,
                                collation),
                            pattern))
                        .Select(sp => sp.ResourceSurrogateId);
                    break;
                }

            case StringOperator.EndsWith:
                {
                    // Build ends-with pattern
                    var pattern = $"%{searchText}";

                    // For ends-with, we need to search the full text including overflow
                    query = baseQuery
                        .Where(sp => EF.Functions.Like(
                            EF.Functions.Collate(
                                sp.TextOverflow != null ? sp.Text + sp.TextOverflow : sp.Text,
                                collation),
                            pattern))
                        .Select(sp => sp.ResourceSurrogateId);
                    break;
                }

            case StringOperator.Equals:
                {
                    // For exact match, compare full concatenated text with collation
                    query = baseQuery
                        .Where(sp => EF.Functions.Collate(
                            sp.TextOverflow != null ? sp.Text + sp.TextOverflow : sp.Text,
                            collation) == searchText)
                        .Select(sp => sp.ResourceSurrogateId);
                    break;
                }

            default:
                throw new NotSupportedException($"StringOperator {stringOperator} is not supported");
        }

        return await Task.FromResult(query);
    }

    private async Task<IQueryable<long>> GenerateTokenQueryAsync(
        short? resourceTypeId,
        short? searchParamId,
        string? system,
        string? code,
        CancellationToken ct)
    {
        int? systemId = null;

        if (!string.IsNullOrEmpty(system))
        {
            systemId = await _cache.GetOrCreateSystemIdAsync(system);
        }

        // When resourceTypeId is null (system-wide search), don't filter by resource type
        // Filter by SearchParamId to only match values indexed for this specific parameter (e.g., _tag, identifier, etc.)
        // FHIR Spec: Token searches are case-insensitive for the code portion.
        // Use case-insensitive collation since the Code column uses Latin1_General_100_CS_AS (case-sensitive).
        var query = _context.TokenSearchParams
            .Where(sp => (!resourceTypeId.HasValue || sp.ResourceTypeId == resourceTypeId.Value)
                && (!searchParamId.HasValue || sp.SearchParamId == searchParamId.Value)
                && (code == null || EF.Functions.Collate(sp.Code, "Latin1_General_100_CI_AS") == code)
                && (systemId == null || sp.SystemId == systemId))
            .Select(sp => sp.ResourceSurrogateId);

        return query;
    }

    /// <summary>
    /// Generates a query against the TokenText table for :text modifier searches.
    /// Searches in the display text (CodeableConcept.text, Coding.display) of token values.
    /// </summary>
    private IQueryable<long> GenerateTokenTextQuery(
        short? resourceTypeId,
        short? searchParamId,
        string text,
        StringOperator stringOperator)
    {
        // When resourceTypeId is null (system-wide search), don't filter by resource type
        // Filter by SearchParamId to only match values indexed for this specific parameter
        // Filter on current resources (not history)
        var query = _context.TokenTexts
            .Where(sp => (!resourceTypeId.HasValue || sp.ResourceTypeId == resourceTypeId.Value)
                && (!searchParamId.HasValue || sp.SearchParamId == searchParamId.Value)
                && !sp.IsHistory);

        // Apply string comparison based on operator (typically StartsWith for :text modifier)
        query = stringOperator switch
        {
            StringOperator.StartsWith => query.Where(sp => sp.Text.StartsWith(text)),
            StringOperator.Contains => query.Where(sp => sp.Text.Contains(text)),
            StringOperator.EndsWith => query.Where(sp => sp.Text.EndsWith(text)),
            StringOperator.Equals => query.Where(sp => sp.Text == text),
            _ => query.Where(sp => sp.Text.StartsWith(text)) // Default to StartsWith for :text
        };

        return query.Select(sp => sp.ResourceSurrogateId);
    }

    private IQueryable<long> GenerateNumberQuery(short? resourceTypeId, short? searchParamId, BinaryExpression binaryExpr)
    {
        var value = Convert.ToDecimal(binaryExpr.Value);

        // When resourceTypeId is null (system-wide search), don't filter by resource type
        // Filter by SearchParamId to only match values indexed for this specific parameter
        var query = _context.NumberSearchParams
            .Where(sp => (!resourceTypeId.HasValue || sp.ResourceTypeId == resourceTypeId.Value)
                && (!searchParamId.HasValue || sp.SearchParamId == searchParamId.Value));

        // Apply comparison based on operator
        query = binaryExpr.BinaryOperator switch
        {
            BinaryOperator.Equal => query.Where(sp => sp.LowValue <= value && sp.HighValue >= value),
            BinaryOperator.GreaterThan => query.Where(sp => sp.LowValue > value),
            BinaryOperator.GreaterThanOrEqual => query.Where(sp => sp.LowValue >= value),
            BinaryOperator.LessThan => query.Where(sp => sp.HighValue < value),
            BinaryOperator.LessThanOrEqual => query.Where(sp => sp.HighValue <= value),
            BinaryOperator.NotEqual => query.Where(sp => sp.HighValue < value || sp.LowValue > value),
            _ => throw new NotSupportedException($"BinaryOperator {binaryExpr.BinaryOperator} is not supported for Number")
        };

        return query.Select(sp => sp.ResourceSurrogateId);
    }

    private IQueryable<long> GenerateDateTimeQuery(short? resourceTypeId, short? searchParamId, BinaryExpression binaryExpr)
    {
        // Handle both DateTime and DateTimeOffset
        DateTime value = binaryExpr.Value switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.UtcDateTime,
            _ => Convert.ToDateTime(binaryExpr.Value)
        };

        _logger.LogDebug(
            "GenerateDateTimeQuery: FieldName={FieldName}, Operator={Operator}, Value={Value}, ResourceTypeId={ResourceTypeId}, SearchParamId={SearchParamId}",
            binaryExpr.FieldName,
            binaryExpr.BinaryOperator,
            value.ToString("o"),
            resourceTypeId,
            searchParamId);

        // When resourceTypeId is null (system-wide search), don't filter by resource type
        // Filter by SearchParamId to only match values indexed for this specific parameter
        var query = _context.DateTimeSearchParams
            .Where(sp => (!resourceTypeId.HasValue || sp.ResourceTypeId == resourceTypeId.Value)
                && (!searchParamId.HasValue || sp.SearchParamId == searchParamId.Value));

        // Apply comparison based on FieldName (Start vs End) and operator
        // The expression parser creates expressions targeting specific fields:
        // - DateTimeStart comparisons filter on sp.StartDateTime
        // - DateTimeEnd comparisons filter on sp.EndDateTime
        query = (binaryExpr.FieldName, binaryExpr.BinaryOperator) switch
        {
            (FieldName.DateTimeStart, BinaryOperator.GreaterThanOrEqual) => query.Where(sp => sp.StartDateTime >= value),
            (FieldName.DateTimeStart, BinaryOperator.GreaterThan) => query.Where(sp => sp.StartDateTime > value),
            (FieldName.DateTimeStart, BinaryOperator.LessThanOrEqual) => query.Where(sp => sp.StartDateTime <= value),
            (FieldName.DateTimeStart, BinaryOperator.LessThan) => query.Where(sp => sp.StartDateTime < value),
            (FieldName.DateTimeStart, BinaryOperator.Equal) => query.Where(sp => sp.StartDateTime == value),
            (FieldName.DateTimeStart, BinaryOperator.NotEqual) => query.Where(sp => sp.StartDateTime != value),

            (FieldName.DateTimeEnd, BinaryOperator.GreaterThanOrEqual) => query.Where(sp => sp.EndDateTime >= value),
            (FieldName.DateTimeEnd, BinaryOperator.GreaterThan) => query.Where(sp => sp.EndDateTime > value),
            (FieldName.DateTimeEnd, BinaryOperator.LessThanOrEqual) => query.Where(sp => sp.EndDateTime <= value),
            (FieldName.DateTimeEnd, BinaryOperator.LessThan) => query.Where(sp => sp.EndDateTime < value),
            (FieldName.DateTimeEnd, BinaryOperator.Equal) => query.Where(sp => sp.EndDateTime == value),
            (FieldName.DateTimeEnd, BinaryOperator.NotEqual) => query.Where(sp => sp.EndDateTime != value),

            _ => throw new NotSupportedException($"DateTime search with FieldName {binaryExpr.FieldName} and BinaryOperator {binaryExpr.BinaryOperator} is not supported")
        };

        return query.Select(sp => sp.ResourceSurrogateId);
    }

    private async Task<IQueryable<long>> GenerateQuantityQueryAsync(
        short? resourceTypeId,
        short? searchParamId,
        BinaryExpression binaryExpr,
        CancellationToken ct)
    {
        var value = Convert.ToDecimal(binaryExpr.Value);

        // Note: System and Code would need to come from additional expressions in a MultiaryExpression
        // For now, we'll just handle the numeric comparison
        // When resourceTypeId is null (system-wide search), don't filter by resource type
        // Filter by SearchParamId to only match values indexed for this specific parameter
        var query = _context.QuantitySearchParams
            .Where(sp => (!resourceTypeId.HasValue || sp.ResourceTypeId == resourceTypeId.Value)
                && (!searchParamId.HasValue || sp.SearchParamId == searchParamId.Value));

        // Apply comparison based on operator
        query = binaryExpr.BinaryOperator switch
        {
            BinaryOperator.Equal => query.Where(sp => sp.LowValue <= value && sp.HighValue >= value),
            BinaryOperator.GreaterThan => query.Where(sp => sp.LowValue > value),
            BinaryOperator.GreaterThanOrEqual => query.Where(sp => sp.LowValue >= value),
            BinaryOperator.LessThan => query.Where(sp => sp.HighValue < value),
            BinaryOperator.LessThanOrEqual => query.Where(sp => sp.HighValue <= value),
            BinaryOperator.NotEqual => query.Where(sp => sp.HighValue < value || sp.LowValue > value),
            _ => throw new NotSupportedException($"BinaryOperator {binaryExpr.BinaryOperator} is not supported for Quantity")
        };

        return await Task.FromResult(query.Select(sp => sp.ResourceSurrogateId));
    }

    /// <summary>
    /// Generates a query for quantity system (unit system URI) filtering.
    /// Looks up the SystemId from the System table and filters QuantitySearchParams by SystemId.
    /// </summary>
    /// <param name="resourceTypeId">The resource type identifier, or null for system-wide search.</param>
    /// <param name="searchParamId">The search parameter identifier.</param>
    /// <param name="systemUri">The system URI to filter by (e.g., "http://unitsofmeasure.org").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A queryable of matching resource surrogate IDs.</returns>
    private async Task<IQueryable<long>> GenerateQuantitySystemQueryAsync(
        short? resourceTypeId,
        short? searchParamId,
        string systemUri,
        CancellationToken ct)
    {
        // Look up SystemId from System table via cache
        // Note: GetOrCreateSystemIdAsync will find existing systems first (won't create during search)
        var systemId = await _cache.GetOrCreateSystemIdAsync(systemUri);

        if (!systemId.HasValue)
        {
            // System not found - return empty result (no matches possible)
            _logger.LogDebug("Quantity system not found: {SystemUri}", systemUri);
            return Enumerable.Empty<long>().AsQueryable();
        }

        // Query QuantitySearchParams filtered by SystemId
        // When resourceTypeId is null (system-wide search), don't filter by resource type
        // Filter by SearchParamId to only match values indexed for this specific parameter
        var query = _context.QuantitySearchParams
            .Where(sp => (!resourceTypeId.HasValue || sp.ResourceTypeId == resourceTypeId.Value)
                && (!searchParamId.HasValue || sp.SearchParamId == searchParamId.Value)
                && sp.SystemId == systemId.Value)
            .Select(sp => sp.ResourceSurrogateId);

        return query;
    }

    /// <summary>
    /// Generates a query for quantity code (unit code) filtering.
    /// Looks up the QuantityCodeId from the QuantityCode table and filters QuantitySearchParams by QuantityCodeId.
    /// </summary>
    /// <param name="resourceTypeId">The resource type identifier, or null for system-wide search.</param>
    /// <param name="searchParamId">The search parameter identifier.</param>
    /// <param name="code">The unit code to filter by (e.g., "mg", "kg").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A queryable of matching resource surrogate IDs.</returns>
    private async Task<IQueryable<long>> GenerateQuantityCodeQueryAsync(
        short? resourceTypeId,
        short? searchParamId,
        string code,
        CancellationToken ct)
    {
        // Look up QuantityCodeId from QuantityCode table via cache
        // Note: GetOrCreateQuantityCodeIdAsync will find existing codes first (won't create during search)
        var quantityCodeId = await _cache.GetOrCreateQuantityCodeIdAsync(code);

        if (!quantityCodeId.HasValue)
        {
            // Code not found - return empty result (no matches possible)
            _logger.LogDebug("Quantity code not found: {Code}", code);
            return Enumerable.Empty<long>().AsQueryable();
        }

        // Query QuantitySearchParams filtered by QuantityCodeId
        // When resourceTypeId is null (system-wide search), don't filter by resource type
        // Filter by SearchParamId to only match values indexed for this specific parameter
        var query = _context.QuantitySearchParams
            .Where(sp => (!resourceTypeId.HasValue || sp.ResourceTypeId == resourceTypeId.Value)
                && (!searchParamId.HasValue || sp.SearchParamId == searchParamId.Value)
                && sp.QuantityCodeId == quantityCodeId.Value)
            .Select(sp => sp.ResourceSurrogateId);

        return query;
    }

    private async Task<IQueryable<long>> GenerateReferenceQueryByIdAsync(
        short? resourceTypeId,
        short? searchParamId,
        string referenceResourceId,
        CancellationToken ct)
    {
        // When resourceTypeId is null (system-wide search), don't filter by resource type
        // Filter by SearchParamId to only match values indexed for this specific parameter
        var query = _context.ReferenceSearchParams
            .Where(sp => (!resourceTypeId.HasValue || sp.ResourceTypeId == resourceTypeId.Value)
                && (!searchParamId.HasValue || sp.SearchParamId == searchParamId.Value)
                && sp.ReferenceResourceId == referenceResourceId)
            .Select(sp => sp.ResourceSurrogateId);

        return await Task.FromResult(query);
    }

    private async Task<IQueryable<long>> GenerateReferenceQueryByTypeAsync(
        short? resourceTypeId,
        short? searchParamId,
        string referenceResourceType,
        CancellationToken ct)
    {
        // Convert resource type name to ID
        var referenceResourceTypeEntity = await _context.ResourceTypes
            .FirstOrDefaultAsync(rt => rt.Name == referenceResourceType, cancellationToken: ct);

        if (referenceResourceTypeEntity == null)
        {
            // Resource type not found - return empty query
            return Enumerable.Empty<long>().AsQueryable();
        }

        // When resourceTypeId is null (system-wide search), don't filter by resource type
        // Filter by SearchParamId to only match values indexed for this specific parameter
        var query = _context.ReferenceSearchParams
            .Where(sp => (!resourceTypeId.HasValue || sp.ResourceTypeId == resourceTypeId.Value)
                && (!searchParamId.HasValue || sp.SearchParamId == searchParamId.Value)
                && sp.ReferenceResourceTypeId == referenceResourceTypeEntity.ResourceTypeId)
            .Select(sp => sp.ResourceSurrogateId);

        return query;
    }

    private IQueryable<long> GenerateUriQuery(
        short? resourceTypeId,
        short? searchParamId,
        string uri,
        StringOperator stringOperator)
    {
        // When resourceTypeId is null (system-wide search), don't filter by resource type
        // Filter by SearchParamId to only match values indexed for this specific parameter
        var query = _context.UriSearchParams
            .Where(sp => (!resourceTypeId.HasValue || sp.ResourceTypeId == resourceTypeId.Value)
                && (!searchParamId.HasValue || sp.SearchParamId == searchParamId.Value));

        // Apply the appropriate URI matching based on the StringOperator
        // FHIR URI search modifiers:
        //   - No modifier (Equals): Exact match
        //   - :above (LeftSideStartsWith): The search value starts with the indexed URI (indexed URI is ancestor)
        //   - :below (StartsWith): The indexed URI starts with the search value (search value is ancestor)
        //   - NotStartsWith: Used in combination expressions to exclude certain URI schemes (e.g., urn:)
        query = stringOperator switch
        {
            StringOperator.Equals => query.Where(sp => sp.Uri == uri),
            StringOperator.LeftSideStartsWith => query.Where(sp => uri.StartsWith(sp.Uri)),
            StringOperator.StartsWith => query.Where(sp => sp.Uri.StartsWith(uri)),
            StringOperator.NotStartsWith => query.Where(sp => !sp.Uri.StartsWith(uri)),
            _ => throw new NotSupportedException($"StringOperator {stringOperator} is not supported for URI search")
        };

        return query.Select(sp => sp.ResourceSurrogateId);
    }
}
