// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
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

        // Process the inner expression based on its type
        return await ProcessExpressionAsync(resourceTypeId, expression.Expression, ct);
    }

    private async Task<IQueryable<long>> ProcessExpressionAsync(
        short resourceTypeId,
        Expression expr,
        CancellationToken ct)
    {
        return expr switch
        {
            MultiaryExpression multiaryExpr => await ProcessMultiaryExpressionAsync(resourceTypeId, multiaryExpr, ct),
            StringExpression stringExpr => await ProcessStringExpressionAsync(resourceTypeId, stringExpr, ct),
            BinaryExpression binaryExpr => await ProcessBinaryExpressionAsync(resourceTypeId, binaryExpr, ct),
            _ => throw new NotSupportedException($"Expression type {expr.GetType().Name} is not supported for query generation")
        };
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

    private async Task<IQueryable<long>> GenerateStringQueryAsync(
        short resourceTypeId,
        string searchText,
        StringOperator stringOperator,
        CancellationToken ct)
    {
        // Build the LIKE pattern based on StringOperator
        var pattern = stringOperator switch
        {
            StringOperator.StartsWith => $"{searchText}%",
            StringOperator.EndsWith => $"%{searchText}",
            StringOperator.Contains => $"%{searchText}%",
            StringOperator.Equals => searchText,
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

    private IQueryable<long> GenerateUriQuery(short resourceTypeId, string uri)
    {
        var query = _context.UriSearchParams
            .Where(sp => sp.ResourceTypeId == resourceTypeId
                && sp.Uri == uri)
            .Select(sp => sp.ResourceSurrogateId);

        return query;
    }
}
