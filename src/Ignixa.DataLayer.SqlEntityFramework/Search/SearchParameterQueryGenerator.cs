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

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchParameterQueryGenerator"/> class.
    /// </summary>
    /// <param name="context">The EF Core DbContext.</param>
    /// <param name="cache">The reference data cache.</param>
    /// <param name="logger">Logger instance.</param>
    public SearchParameterQueryGenerator(
        FhirDbContext context,
        SearchIndexReferenceDataCache cache,
        ILogger<SearchParameterQueryGenerator> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Generates a query for a search parameter expression, returning matching resource surrogate IDs.
    /// </summary>
    /// <param name="resourceTypeId">The resource type identifier.</param>
    /// <param name="expression">The search parameter expression.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A queryable of matching resource surrogate IDs.</returns>
    public async Task<IQueryable<long>> GenerateQueryAsync(
        short resourceTypeId,
        SearchParameterExpression expression,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(expression);

        _logger.LogDebug("Generating query for search parameter: {Parameter}", expression.Parameter?.Name);

        // Handle resource-level parameters that query Resource table directly
        // instead of indexed search parameter tables
        if (expression.Parameter?.Code == "_id")
        {
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

        // TODO: other resource-level parameters like _profile, _security, _tag also need special handling

        // Process the inner expression based on its type
        return await ProcessExpressionAsync(resourceTypeId, expression.Expression, ct);
    }

    /// <summary>
    /// Processes _id parameter expressions by querying the Resource table directly.
    /// The _id parameter is a resource-level parameter that matches against Resource.ResourceId.
    /// </summary>
    private async Task<IQueryable<long>> ProcessResourceIdExpressionAsync(
        short resourceTypeId,
        Expression expr,
        CancellationToken ct)
    {
        _logger.LogDebug("Processing _id parameter expression");

        // The _id parameter is defined as TokenCode field, so we need to extract the resource ID value
        if (expr is StringExpression stringExpr && stringExpr.FieldName == FieldName.TokenCode)
        {
            // Query the Resource table: WHERE ResourceTypeId = ? AND ResourceId = ?
            var query = _context.Resources
                .Where(r => r.ResourceTypeId == resourceTypeId
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

        throw new NotSupportedException($"Expression type {expr.GetType().Name} is not supported for _id parameter query generation");
    }

    /// <summary>
    /// Processes multiary expressions (e.g., multiple _id values) for resource-level parameters.
    /// </summary>
    private Task<IQueryable<long>> ProcessResourceIdMultiaryExpressionAsync(
        short resourceTypeId,
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
            if (subExpr is StringExpression stringExpr && stringExpr.FieldName == FieldName.TokenCode)
            {
                var query = _context.Resources
                    .Where(r => r.ResourceTypeId == resourceTypeId
                        && r.ResourceId == stringExpr.Value
                        && !r.IsHistory
                        && !r.IsDeleted)
                    .Select(r => r.ResourceSurrogateId);
                queries.Add(query);
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
    /// Processes _lastUpdated parameter expressions by querying the Resource table directly.
    /// The _lastUpdated parameter is stored as resourceSurrogateId, which encodes the DateTime via IdHelper.ToId().
    /// Compares resourceSurrogateId directly against the value produced by IdHelper.ToId() for the target DateTime.
    /// </summary>
    private async Task<IQueryable<long>> ProcessResourceLastUpdatedExpressionAsync(
        short resourceTypeId,
        Expression expr,
        CancellationToken ct)
    {
        _logger.LogDebug("Processing _lastUpdated parameter expression");

        // The _lastUpdated parameter uses BinaryExpression for comparison operators (>, >=, <, <=, =, !=)
        if (expr is BinaryExpression binaryExpr && binaryExpr.Value is DateTimeOffset dateTimeValue)
        {
            // Query the Resource table: WHERE ResourceTypeId = ? AND resourceSurrogateId [operator] ToId(dateTime)
            var targetId = dateTimeValue.ToId();

            var query = binaryExpr.BinaryOperator switch
            {
                BinaryOperator.Equal =>
                    _context.Resources
                        .Where(r => r.ResourceTypeId == resourceTypeId
                            && r.ResourceSurrogateId == targetId
                            && !r.IsHistory
                            && !r.IsDeleted),
                BinaryOperator.GreaterThan =>
                    _context.Resources
                        .Where(r => r.ResourceTypeId == resourceTypeId
                            && r.ResourceSurrogateId > targetId
                            && !r.IsHistory
                            && !r.IsDeleted),
                BinaryOperator.GreaterThanOrEqual =>
                    _context.Resources
                        .Where(r => r.ResourceTypeId == resourceTypeId
                            && r.ResourceSurrogateId >= targetId
                            && !r.IsHistory
                            && !r.IsDeleted),
                BinaryOperator.LessThan =>
                    _context.Resources
                        .Where(r => r.ResourceTypeId == resourceTypeId
                            && r.ResourceSurrogateId < targetId
                            && !r.IsHistory
                            && !r.IsDeleted),
                BinaryOperator.LessThanOrEqual =>
                    _context.Resources
                        .Where(r => r.ResourceTypeId == resourceTypeId
                            && r.ResourceSurrogateId <= targetId
                            && !r.IsHistory
                            && !r.IsDeleted),
                BinaryOperator.NotEqual =>
                    _context.Resources
                        .Where(r => r.ResourceTypeId == resourceTypeId
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
        short resourceTypeId,
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

                var query = binaryExpr.BinaryOperator switch
                {
                    BinaryOperator.Equal =>
                        _context.Resources
                            .Where(r => r.ResourceTypeId == resourceTypeId
                                && r.ResourceSurrogateId == targetId
                                && !r.IsHistory
                                && !r.IsDeleted),
                    BinaryOperator.GreaterThan =>
                        _context.Resources
                            .Where(r => r.ResourceTypeId == resourceTypeId
                                && r.ResourceSurrogateId > targetId
                                && !r.IsHistory
                                && !r.IsDeleted),
                    BinaryOperator.GreaterThanOrEqual =>
                        _context.Resources
                            .Where(r => r.ResourceTypeId == resourceTypeId
                                && r.ResourceSurrogateId >= targetId
                                && !r.IsHistory
                                && !r.IsDeleted),
                    BinaryOperator.LessThan =>
                        _context.Resources
                            .Where(r => r.ResourceTypeId == resourceTypeId
                                && r.ResourceSurrogateId < targetId
                                && !r.IsHistory
                                && !r.IsDeleted),
                    BinaryOperator.LessThanOrEqual =>
                        _context.Resources
                            .Where(r => r.ResourceTypeId == resourceTypeId
                                && r.ResourceSurrogateId <= targetId
                                && !r.IsHistory
                                && !r.IsDeleted),
                    BinaryOperator.NotEqual =>
                        _context.Resources
                            .Where(r => r.ResourceTypeId == resourceTypeId
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
        short resourceTypeId,
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

    private async Task<IQueryable<long>> ProcessExpressionAsync(
        short resourceTypeId,
        Expression expr,
        CancellationToken ct)
    {
        if (expr is MultiaryExpression multiaryExpr)
        {
            return await ProcessMultiaryExpressionAsync(resourceTypeId, multiaryExpr, ct);
        }

        if (expr is StringExpression stringExpr)
        {
            return await ProcessStringExpressionAsync(resourceTypeId, stringExpr, ct);
        }

        if (expr is BinaryExpression binaryExpr)
        {
            return await ProcessBinaryExpressionAsync(resourceTypeId, binaryExpr, ct);
        }

        if (expr is NotExpression notExpr)
        {
            return await ProcessNotExpressionAsync(resourceTypeId, notExpr, ct);
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
        short resourceTypeId,
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
        short resourceTypeId,
        MultiaryExpression multiaryExpr,
        CancellationToken ct)
    {
        if (multiaryExpr.Expressions.Count == 0)
        {
            return Enumerable.Empty<long>().AsQueryable();
        }

        // Process each sub-expression
        var queries = new List<IQueryable<long>>();
        foreach (var subExpr in multiaryExpr.Expressions)
        {
            var query = await ProcessExpressionAsync(resourceTypeId, subExpr, ct);
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

    private async Task<IQueryable<long>> ProcessStringExpressionAsync(
        short resourceTypeId,
        StringExpression stringExpr,
        CancellationToken ct)
    {
        // Determine which table to query based on FieldName
        return stringExpr.FieldName switch
        {
            FieldName.String => await GenerateStringQueryAsync(resourceTypeId, stringExpr.Value, stringExpr.StringOperator, ct),
            FieldName.Uri => GenerateUriQuery(resourceTypeId, stringExpr.Value),
            FieldName.TokenCode => await GenerateTokenQueryAsync(resourceTypeId, null, stringExpr.Value, ct),
            FieldName.TokenSystem => await GenerateTokenQueryAsync(resourceTypeId, stringExpr.Value, null, ct),
            FieldName.ReferenceResourceId => await GenerateReferenceQueryByIdAsync(resourceTypeId, stringExpr.Value, ct),
            FieldName.ReferenceResourceType => await GenerateReferenceQueryByTypeAsync(resourceTypeId, stringExpr.Value, ct),
            _ => throw new NotSupportedException($"StringExpression with FieldName {stringExpr.FieldName} is not supported")
        };
    }

    private async Task<IQueryable<long>> ProcessBinaryExpressionAsync(
        short resourceTypeId,
        BinaryExpression binaryExpr,
        CancellationToken ct)
    {
        // Determine which table to query based on FieldName
        return binaryExpr.FieldName switch
        {
            FieldName.Number => GenerateNumberQuery(resourceTypeId, binaryExpr),
            FieldName.DateTimeStart or FieldName.DateTimeEnd => GenerateDateTimeQuery(resourceTypeId, binaryExpr),
            FieldName.Quantity => await GenerateQuantityQueryAsync(resourceTypeId, binaryExpr, ct),
            _ => throw new NotSupportedException($"BinaryExpression with FieldName {binaryExpr.FieldName} is not supported")
        };
    }

    private async Task<IQueryable<long>> ProcessNotExpressionAsync(
        short resourceTypeId,
        NotExpression notExpr,
        CancellationToken ct)
    {
        _logger.LogDebug("Processing NOT expression");

        // Process the inner expression to get matching resource IDs
        var innerMatchingIds = await ProcessExpressionAsync(resourceTypeId, notExpr.Expression, ct);

        // Get all resources of this type (non-history, non-deleted)
        var allResourceIds = _context.Resources
            .Where(r => r.ResourceTypeId == resourceTypeId && !r.IsHistory && !r.IsDeleted)
            .Select(r => r.ResourceSurrogateId);

        // Return resources NOT in the inner matching set
        return allResourceIds.Where(id => !innerMatchingIds.Contains(id));
    }

    private async Task<IQueryable<long>> GenerateStringQueryAsync(
        short resourceTypeId,
        string searchText,
        StringOperator stringOperator,
        CancellationToken ct)
    {
        // Normalize search text to uppercase for case-insensitive matching
        var normalizedText = searchText.ToUpperInvariant();

        // Build the LIKE pattern based on StringOperator
        var pattern = stringOperator switch
        {
            StringOperator.StartsWith => $"{normalizedText}%",
            StringOperator.EndsWith => $"%{normalizedText}",
            StringOperator.Contains => $"%{normalizedText}%",
            StringOperator.Equals => normalizedText,
            _ => throw new NotSupportedException($"StringOperator {stringOperator} is not supported")
        };

        var query = _context.StringSearchParams
            .Where(sp => sp.ResourceTypeId == resourceTypeId
                && EF.Functions.Like(sp.Text, pattern))
            .Select(sp => sp.ResourceSurrogateId);

        return await Task.FromResult(query);
    }

    private async Task<IQueryable<long>> GenerateTokenQueryAsync(
        short resourceTypeId,
        string? system,
        string? code,
        CancellationToken ct)
    {
        int? systemId = null;

        if (!string.IsNullOrEmpty(system))
        {
            systemId = await _cache.GetOrCreateSystemIdAsync(system);
        }

        var query = _context.TokenSearchParams
            .Where(sp => sp.ResourceTypeId == resourceTypeId
                && (code == null || sp.Code == code)
                && (systemId == null || sp.SystemId == systemId))
            .Select(sp => sp.ResourceSurrogateId);

        return query;
    }

    private IQueryable<long> GenerateNumberQuery(short resourceTypeId, BinaryExpression binaryExpr)
    {
        var value = Convert.ToDecimal(binaryExpr.Value);

        var query = _context.NumberSearchParams
            .Where(sp => sp.ResourceTypeId == resourceTypeId);

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

    private IQueryable<long> GenerateDateTimeQuery(short resourceTypeId, BinaryExpression binaryExpr)
    {
        // Handle both DateTime and DateTimeOffset
        DateTime value = binaryExpr.Value switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.UtcDateTime,
            _ => Convert.ToDateTime(binaryExpr.Value)
        };

        var query = _context.DateTimeSearchParams
            .Where(sp => sp.ResourceTypeId == resourceTypeId);

        // Apply comparison based on operator (range overlap logic for DateTime)
        query = binaryExpr.BinaryOperator switch
        {
            BinaryOperator.Equal => query.Where(sp => sp.StartDateTime <= value && sp.EndDateTime >= value),
            BinaryOperator.GreaterThan => query.Where(sp => sp.StartDateTime > value),
            BinaryOperator.GreaterThanOrEqual => query.Where(sp => sp.StartDateTime >= value),
            BinaryOperator.LessThan => query.Where(sp => sp.EndDateTime < value),
            BinaryOperator.LessThanOrEqual => query.Where(sp => sp.EndDateTime <= value),
            BinaryOperator.NotEqual => query.Where(sp => sp.EndDateTime < value || sp.StartDateTime > value),
            _ => throw new NotSupportedException($"BinaryOperator {binaryExpr.BinaryOperator} is not supported for DateTime")
        };

        return query.Select(sp => sp.ResourceSurrogateId);
    }

    private async Task<IQueryable<long>> GenerateQuantityQueryAsync(
        short resourceTypeId,
        BinaryExpression binaryExpr,
        CancellationToken ct)
    {
        var value = Convert.ToDecimal(binaryExpr.Value);

        // Note: System and Code would need to come from additional expressions in a MultiaryExpression
        // For now, we'll just handle the numeric comparison
        var query = _context.QuantitySearchParams
            .Where(sp => sp.ResourceTypeId == resourceTypeId);

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

    private async Task<IQueryable<long>> GenerateReferenceQueryByIdAsync(
        short resourceTypeId,
        string referenceResourceId,
        CancellationToken ct)
    {
        var query = _context.ReferenceSearchParams
            .Where(sp => sp.ResourceTypeId == resourceTypeId
                && sp.ReferenceResourceId == referenceResourceId)
            .Select(sp => sp.ResourceSurrogateId);

        return await Task.FromResult(query);
    }

    private async Task<IQueryable<long>> GenerateReferenceQueryByTypeAsync(
        short resourceTypeId,
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

        var query = _context.ReferenceSearchParams
            .Where(sp => sp.ResourceTypeId == resourceTypeId
                && sp.ReferenceResourceTypeId == referenceResourceTypeEntity.ResourceTypeId)
            .Select(sp => sp.ResourceSurrogateId);

        return query;
    }

    private IQueryable<long> GenerateUriQuery(short resourceTypeId, string uri)
    {
        var query = _context.UriSearchParams
            .Where(sp => sp.ResourceTypeId == resourceTypeId
                && sp.Uri == uri)
            .Select(sp => sp.ResourceSurrogateId);

        return query;
    }
}
