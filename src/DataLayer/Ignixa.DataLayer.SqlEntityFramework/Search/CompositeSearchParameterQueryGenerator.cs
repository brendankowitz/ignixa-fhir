// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.DataLayer.SqlEntityFramework.Search;

/// <summary>
/// Generates EF Core queries for composite search parameters.
/// Routes to the appropriate composite table (TokenToken, TokenQuantity, etc.) based on search parameter type.
/// </summary>
public class CompositeSearchParameterQueryGenerator
{
    private readonly FhirDbContext _context;
    private readonly SearchIndexReferenceDataCache _cache;
    private readonly ILogger<CompositeSearchParameterQueryGenerator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeSearchParameterQueryGenerator"/> class.
    /// </summary>
    /// <param name="context">The EF Core DbContext.</param>
    /// <param name="cache">The reference data cache.</param>
    /// <param name="logger">Logger instance.</param>
    public CompositeSearchParameterQueryGenerator(
        FhirDbContext context,
        SearchIndexReferenceDataCache cache,
        ILogger<CompositeSearchParameterQueryGenerator> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Determines the composite type from search parameter component types.
    /// </summary>
    /// <param name="searchParam">The composite search parameter.</param>
    /// <returns>The composite type enum value.</returns>
    public CompositeType DetermineCompositeType(SearchParameterInfo searchParam)
    {
        if (searchParam.Component == null || searchParam.Component.Count < 2)
        {
            return CompositeType.Unknown;
        }

        var types = searchParam.Component
            .Select(c => c.ResolvedSearchParameter?.Type)
            .ToList();

        // Token|Token (combo-code-value-concept)
        if (types.Count == 2 &&
            types[0] == SearchParamType.Token &&
            types[1] == SearchParamType.Token)
        {
            return CompositeType.TokenToken;
        }

        // Token|Quantity (code-value-quantity, combo-code-value-quantity)
        if (types.Count == 2 &&
            types[0] == SearchParamType.Token &&
            types[1] == SearchParamType.Quantity)
        {
            return CompositeType.TokenQuantity;
        }

        // Token|DateTime
        if (types.Count == 2 &&
            types[0] == SearchParamType.Token &&
            types[1] == SearchParamType.Date)
        {
            return CompositeType.TokenDateTime;
        }

        // Token|String (code-value-string)
        if (types.Count == 2 &&
            types[0] == SearchParamType.Token &&
            types[1] == SearchParamType.String)
        {
            return CompositeType.TokenString;
        }

        // Reference|Token (relationship on DocumentReference)
        if (types.Count == 2 &&
            types[0] == SearchParamType.Reference &&
            types[1] == SearchParamType.Token)
        {
            return CompositeType.ReferenceToken;
        }

        // Token|Number|Number (MolecularSequence)
        if (types.Count == 3 &&
            types[0] == SearchParamType.Token &&
            types[1] == SearchParamType.Number &&
            types[2] == SearchParamType.Number)
        {
            return CompositeType.TokenNumberNumber;
        }

        return CompositeType.Unknown;
    }

    /// <summary>
    /// Generates a query for a Token|Token composite search parameter.
    /// </summary>
    /// <param name="resourceTypeId">The resource type identifier, or null for system-wide search.</param>
    /// <param name="searchParamId">The search parameter identifier.</param>
    /// <param name="component0">Expression for the first component.</param>
    /// <param name="component1">Expression for the second component.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A queryable of matching resource surrogate IDs.</returns>
    public async Task<IQueryable<long>> GenerateTokenTokenQueryAsync(
        short? resourceTypeId,
        short searchParamId,
        Expression component0,
        Expression component1,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Generating Token|Token composite query for SearchParamId={SearchParamId}", searchParamId);

        // Extract token values from expressions
        var token1 = ExtractTokenValues(component0);
        var token2 = ExtractTokenValues(component1);

        // Look up system IDs if systems are specified
        int? systemId1 = null;
        int? systemId2 = null;

        if (!string.IsNullOrEmpty(token1.System))
        {
            systemId1 = await _cache.GetOrCreateSystemIdAsync(token1.System);
        }

        if (!string.IsNullOrEmpty(token2.System))
        {
            systemId2 = await _cache.GetOrCreateSystemIdAsync(token2.System);
        }

        // Build query against TokenTokenCompositeSearchParam table
        var query = _context.TokenTokenCompositeSearchParams
            .Where(t => t.SearchParamId == searchParamId);

        // Apply resource type filter if specified
        if (resourceTypeId.HasValue)
        {
            query = query.Where(t => t.ResourceTypeId == resourceTypeId.Value);
        }

        // Apply first component filter
        if (!string.IsNullOrEmpty(token1.Code))
        {
            query = query.Where(t => t.Code1 == token1.Code);
        }

        if (systemId1.HasValue)
        {
            query = query.Where(t => t.SystemId1 == systemId1.Value);
        }
        else if (token1.SystemIsEmpty)
        {
            // Explicit empty system: match NULL system
            query = query.Where(t => t.SystemId1 == null);
        }

        // Apply second component filter
        if (!string.IsNullOrEmpty(token2.Code))
        {
            query = query.Where(t => t.Code2 == token2.Code);
        }

        if (systemId2.HasValue)
        {
            query = query.Where(t => t.SystemId2 == systemId2.Value);
        }
        else if (token2.SystemIsEmpty)
        {
            // Explicit empty system: match NULL system
            query = query.Where(t => t.SystemId2 == null);
        }

        return query.Select(t => t.ResourceSurrogateId);
    }

    /// <summary>
    /// Generates a query for a Token|Quantity composite search parameter.
    /// </summary>
    public async Task<IQueryable<long>> GenerateTokenQuantityQueryAsync(
        short? resourceTypeId,
        short searchParamId,
        Expression component0,
        Expression component1,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Generating Token|Quantity composite query for SearchParamId={SearchParamId}", searchParamId);

        // Extract token values from first component
        var token = ExtractTokenValues(component0);
        int? systemId1 = null;

        if (!string.IsNullOrEmpty(token.System))
        {
            systemId1 = await _cache.GetOrCreateSystemIdAsync(token.System);
        }

        // Build base query
        var query = _context.TokenQuantityCompositeSearchParams
            .Where(t => t.SearchParamId == searchParamId);

        if (resourceTypeId.HasValue)
        {
            query = query.Where(t => t.ResourceTypeId == resourceTypeId.Value);
        }

        // Apply first component (token) filter
        if (!string.IsNullOrEmpty(token.Code))
        {
            query = query.Where(t => t.Code1 == token.Code);
        }

        if (systemId1.HasValue)
        {
            query = query.Where(t => t.SystemId1 == systemId1.Value);
        }
        else if (token.SystemIsEmpty)
        {
            query = query.Where(t => t.SystemId1 == null);
        }

        // Apply second component (quantity) filter
        query = await ApplyQuantityFilterAsync(query, component1, cancellationToken);

        return query.Select(t => t.ResourceSurrogateId);
    }

    /// <summary>
    /// Generates a query for a Token|String composite search parameter.
    /// </summary>
    public async Task<IQueryable<long>> GenerateTokenStringQueryAsync(
        short? resourceTypeId,
        short searchParamId,
        Expression component0,
        Expression component1,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Generating Token|String composite query for SearchParamId={SearchParamId}", searchParamId);

        // Extract token values from first component
        var token = ExtractTokenValues(component0);
        int? systemId1 = null;

        if (!string.IsNullOrEmpty(token.System))
        {
            systemId1 = await _cache.GetOrCreateSystemIdAsync(token.System);
        }

        // Build base query
        var query = _context.TokenStringCompositeSearchParams
            .Where(t => t.SearchParamId == searchParamId);

        if (resourceTypeId.HasValue)
        {
            query = query.Where(t => t.ResourceTypeId == resourceTypeId.Value);
        }

        // Apply first component (token) filter
        if (!string.IsNullOrEmpty(token.Code))
        {
            query = query.Where(t => t.Code1 == token.Code);
        }

        if (systemId1.HasValue)
        {
            query = query.Where(t => t.SystemId1 == systemId1.Value);
        }
        else if (token.SystemIsEmpty)
        {
            query = query.Where(t => t.SystemId1 == null);
        }

        // Apply second component (string) filter
        var stringValue = ExtractStringValue(component1);
        if (!string.IsNullOrEmpty(stringValue))
        {
            var normalizedValue = stringValue.ToUpperInvariant();
            query = query.Where(t => t.Text2.StartsWith(normalizedValue));
        }

        return query.Select(t => t.ResourceSurrogateId);
    }

    /// <summary>
    /// Generates a query for a Reference|Token composite search parameter.
    /// </summary>
    public async Task<IQueryable<long>> GenerateReferenceTokenQueryAsync(
        short? resourceTypeId,
        short searchParamId,
        Expression component0,
        Expression component1,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Generating Reference|Token composite query for SearchParamId={SearchParamId}", searchParamId);

        // Extract reference from first component
        var reference = ExtractReferenceValue(component0);

        // Extract token from second component
        var token = ExtractTokenValues(component1);
        int? systemId2 = null;

        if (!string.IsNullOrEmpty(token.System))
        {
            systemId2 = await _cache.GetOrCreateSystemIdAsync(token.System);
        }

        // Build base query
        var query = _context.ReferenceTokenCompositeSearchParams
            .Where(r => r.SearchParamId == searchParamId);

        if (resourceTypeId.HasValue)
        {
            query = query.Where(r => r.ResourceTypeId == resourceTypeId.Value);
        }

        // Apply first component (reference) filter
        if (!string.IsNullOrEmpty(reference.ResourceId))
        {
            query = query.Where(r => r.ReferenceResourceId1 == reference.ResourceId);
        }

        // Apply second component (token) filter
        if (!string.IsNullOrEmpty(token.Code))
        {
            query = query.Where(r => r.Code2 == token.Code);
        }

        if (systemId2.HasValue)
        {
            query = query.Where(r => r.SystemId2 == systemId2.Value);
        }
        else if (token.SystemIsEmpty)
        {
            query = query.Where(r => r.SystemId2 == null);
        }

        return query.Select(r => r.ResourceSurrogateId);
    }

    /// <summary>
    /// Generates a query for a Token|DateTime composite search parameter.
    /// </summary>
    public async Task<IQueryable<long>> GenerateTokenDateTimeQueryAsync(
        short? resourceTypeId,
        short searchParamId,
        Expression component0,
        Expression component1,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Generating Token|DateTime composite query for SearchParamId={SearchParamId}", searchParamId);

        // Extract token values from first component
        var token = ExtractTokenValues(component0);
        int? systemId1 = null;

        if (!string.IsNullOrEmpty(token.System))
        {
            systemId1 = await _cache.GetOrCreateSystemIdAsync(token.System);
        }

        // Build base query
        var query = _context.TokenDateTimeCompositeSearchParams
            .Where(t => t.SearchParamId == searchParamId);

        if (resourceTypeId.HasValue)
        {
            query = query.Where(t => t.ResourceTypeId == resourceTypeId.Value);
        }

        // Apply first component (token) filter
        if (!string.IsNullOrEmpty(token.Code))
        {
            query = query.Where(t => t.Code1 == token.Code);
        }

        if (systemId1.HasValue)
        {
            query = query.Where(t => t.SystemId1 == systemId1.Value);
        }
        else if (token.SystemIsEmpty)
        {
            query = query.Where(t => t.SystemId1 == null);
        }

        // Apply second component (datetime) filter
        query = ApplyDateTimeFilter(query, component1);

        return query.Select(t => t.ResourceSurrogateId);
    }

    private (string? System, string? Code, bool SystemIsEmpty) ExtractTokenValues(Expression expression)
    {
        string? system = null;
        string? code = null;
        bool systemIsEmpty = false;

        if (expression is MultiaryExpression multiary)
        {
            foreach (var subExpr in multiary.Expressions)
            {
                var result = ExtractTokenValuesFromSingle(subExpr);
                if (result.System != null) system = result.System;
                if (result.Code != null) code = result.Code;
                if (result.SystemIsEmpty) systemIsEmpty = true;
            }
        }
        else
        {
            return ExtractTokenValuesFromSingle(expression);
        }

        return (system, code, systemIsEmpty);
    }

    private (string? System, string? Code, bool SystemIsEmpty) ExtractTokenValuesFromSingle(Expression expression)
    {
        if (expression is StringExpression stringExpr)
        {
            if (stringExpr.FieldName == FieldName.TokenCode)
            {
                return (null, stringExpr.Value, false);
            }
            else if (stringExpr.FieldName == FieldName.TokenSystem)
            {
                // Empty string means explicitly no system
                bool isEmpty = string.IsNullOrEmpty(stringExpr.Value);
                return (isEmpty ? null : stringExpr.Value, null, isEmpty);
            }
        }
        else if (expression is MissingFieldExpression missingExpr)
        {
            if (missingExpr.FieldName == FieldName.TokenSystem)
            {
                return (null, null, true);
            }
        }

        return (null, null, false);
    }

    private string? ExtractStringValue(Expression expression)
    {
        if (expression is StringExpression stringExpr && stringExpr.FieldName == FieldName.String)
        {
            return stringExpr.Value;
        }

        if (expression is MultiaryExpression multiary)
        {
            foreach (var subExpr in multiary.Expressions)
            {
                var value = ExtractStringValue(subExpr);
                if (value != null) return value;
            }
        }

        return null;
    }

    private (string? ResourceType, string? ResourceId) ExtractReferenceValue(Expression expression)
    {
        string? resourceType = null;
        string? resourceId = null;

        if (expression is MultiaryExpression multiary)
        {
            foreach (var subExpr in multiary.Expressions)
            {
                if (subExpr is StringExpression stringExpr)
                {
                    if (stringExpr.FieldName == FieldName.ReferenceResourceType)
                    {
                        resourceType = stringExpr.Value;
                    }
                    else if (stringExpr.FieldName == FieldName.ReferenceResourceId)
                    {
                        resourceId = stringExpr.Value;
                    }
                }
            }
        }
        else if (expression is StringExpression stringExpr)
        {
            if (stringExpr.FieldName == FieldName.ReferenceResourceId)
            {
                resourceId = stringExpr.Value;
            }
        }

        return (resourceType, resourceId);
    }

    private async Task<IQueryable<Entities.TokenQuantityCompositeSearchParamEntity>> ApplyQuantityFilterAsync(
        IQueryable<Entities.TokenQuantityCompositeSearchParamEntity> query,
        Expression expression,
        CancellationToken cancellationToken)
    {
        // Extract quantity components (value, system, code)
        decimal? quantityValue = null;
        string? quantitySystem = null;
        string? quantityCode = null;
        BinaryOperator? binaryOp = null;

        void ProcessExpression(Expression expr)
        {
            if (expr is BinaryExpression binaryExpr && binaryExpr.FieldName == FieldName.Quantity)
            {
                quantityValue = Convert.ToDecimal(binaryExpr.Value);
                binaryOp = binaryExpr.BinaryOperator;
            }
            else if (expr is StringExpression stringExpr)
            {
                if (stringExpr.FieldName == FieldName.QuantitySystem)
                {
                    quantitySystem = stringExpr.Value;
                }
                else if (stringExpr.FieldName == FieldName.QuantityCode)
                {
                    quantityCode = stringExpr.Value;
                }
            }
            else if (expr is MultiaryExpression multiary)
            {
                foreach (var subExpr in multiary.Expressions)
                {
                    ProcessExpression(subExpr);
                }
            }
        }

        ProcessExpression(expression);

        // Apply system filter
        if (!string.IsNullOrEmpty(quantitySystem))
        {
            var systemId = await _cache.GetOrCreateSystemIdAsync(quantitySystem);
            if (systemId.HasValue)
            {
                query = query.Where(q => q.SystemId2 == systemId.Value);
            }
        }

        // Apply code filter
        if (!string.IsNullOrEmpty(quantityCode))
        {
            var codeId = await _cache.GetOrCreateQuantityCodeIdAsync(quantityCode);
            if (codeId.HasValue)
            {
                query = query.Where(q => q.QuantityCodeId == codeId.Value);
            }
        }

        // Apply value filter
        if (quantityValue.HasValue)
        {
            var value = quantityValue.Value;
            query = (binaryOp ?? BinaryOperator.Equal) switch
            {
                BinaryOperator.Equal => query.Where(q => q.LowValue <= value && q.HighValue >= value),
                BinaryOperator.GreaterThan => query.Where(q => q.LowValue > value),
                BinaryOperator.GreaterThanOrEqual => query.Where(q => q.LowValue >= value),
                BinaryOperator.LessThan => query.Where(q => q.HighValue < value),
                BinaryOperator.LessThanOrEqual => query.Where(q => q.HighValue <= value),
                BinaryOperator.NotEqual => query.Where(q => q.HighValue < value || q.LowValue > value),
                _ => query
            };
        }

        return query;
    }

    private IQueryable<Entities.TokenDateTimeCompositeSearchParamEntity> ApplyDateTimeFilter(
        IQueryable<Entities.TokenDateTimeCompositeSearchParamEntity> query,
        Expression expression)
    {
        // Extract datetime components
        DateTime? startValue = null;
        DateTime? endValue = null;
        BinaryOperator? startOp = null;
        BinaryOperator? endOp = null;

        void ProcessExpression(Expression expr)
        {
            if (expr is BinaryExpression binaryExpr)
            {
                var dateValue = binaryExpr.Value switch
                {
                    DateTime dt => dt,
                    DateTimeOffset dto => dto.UtcDateTime,
                    _ => default(DateTime?)
                };

                if (dateValue.HasValue)
                {
                    if (binaryExpr.FieldName == FieldName.DateTimeStart)
                    {
                        startValue = dateValue;
                        startOp = binaryExpr.BinaryOperator;
                    }
                    else if (binaryExpr.FieldName == FieldName.DateTimeEnd)
                    {
                        endValue = dateValue;
                        endOp = binaryExpr.BinaryOperator;
                    }
                }
            }
            else if (expr is MultiaryExpression multiary)
            {
                foreach (var subExpr in multiary.Expressions)
                {
                    ProcessExpression(subExpr);
                }
            }
        }

        ProcessExpression(expression);

        // Apply start datetime filter
        if (startValue.HasValue && startOp.HasValue)
        {
            var value = startValue.Value;
            query = startOp.Value switch
            {
                BinaryOperator.GreaterThanOrEqual => query.Where(t => t.StartDateTime2 >= value),
                BinaryOperator.GreaterThan => query.Where(t => t.StartDateTime2 > value),
                BinaryOperator.LessThanOrEqual => query.Where(t => t.StartDateTime2 <= value),
                BinaryOperator.LessThan => query.Where(t => t.StartDateTime2 < value),
                BinaryOperator.Equal => query.Where(t => t.StartDateTime2 == value),
                _ => query
            };
        }

        // Apply end datetime filter
        if (endValue.HasValue && endOp.HasValue)
        {
            var value = endValue.Value;
            query = endOp.Value switch
            {
                BinaryOperator.GreaterThanOrEqual => query.Where(t => t.EndDateTime2 >= value),
                BinaryOperator.GreaterThan => query.Where(t => t.EndDateTime2 > value),
                BinaryOperator.LessThanOrEqual => query.Where(t => t.EndDateTime2 <= value),
                BinaryOperator.LessThan => query.Where(t => t.EndDateTime2 < value),
                BinaryOperator.Equal => query.Where(t => t.EndDateTime2 == value),
                _ => query
            };
        }

        return query;
    }
}

/// <summary>
/// Enum representing the type of composite search parameter.
/// </summary>
public enum CompositeType
{
    /// <summary>Unknown or unsupported composite type.</summary>
    Unknown,

    /// <summary>Token|Token composite (combo-code-value-concept).</summary>
    TokenToken,

    /// <summary>Token|Quantity composite (code-value-quantity).</summary>
    TokenQuantity,

    /// <summary>Token|DateTime composite.</summary>
    TokenDateTime,

    /// <summary>Token|String composite (code-value-string).</summary>
    TokenString,

    /// <summary>Reference|Token composite (relationship on DocumentReference).</summary>
    ReferenceToken,

    /// <summary>Token|Number|Number composite (MolecularSequence).</summary>
    TokenNumberNumber
}
