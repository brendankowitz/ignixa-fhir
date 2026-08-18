// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.DataLayer.SqlEntityFramework.RowGenerators;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

using DateTimeCompositePredicate = System.Linq.Expressions.Expression<System.Func<Ignixa.DataLayer.SqlEntityFramework.Entities.TokenDateTimeCompositeSearchParamEntity, bool>>;

namespace Ignixa.DataLayer.SqlEntityFramework.Search;

/// <summary>
/// Generates EF Core queries for composite search parameters.
/// Routes to the appropriate composite table (TokenToken, TokenQuantity, etc.) based on search parameter type.
/// </summary>
public class CompositeSearchParameterQueryGenerator
{
    // Each slot resolves its own column's declared width, the same way the row generators that write it and
    // the lowering rules that compile against it do. Above that width the value is stored split, so the
    // predicate has to reassemble it; at or below it the overflow column must be empty, or an exact-width
    // search would also match the truncated head of a longer code.
    private static readonly int TokenTokenCode1Width = SearchParamColumnWidths.For("TokenTokenCompositeSearchParam", "Code1");
    private static readonly int TokenTokenCode2Width = SearchParamColumnWidths.For("TokenTokenCompositeSearchParam", "Code2");
    private static readonly int TokenQuantityCode1Width = SearchParamColumnWidths.For("TokenQuantityCompositeSearchParam", "Code1");
    private static readonly int TokenStringCode1Width = SearchParamColumnWidths.For("TokenStringCompositeSearchParam", "Code1");
    private static readonly int TokenStringText2Width = SearchParamColumnWidths.For("TokenStringCompositeSearchParam", "Text2");
    private static readonly int TokenDateTimeCode1Width = SearchParamColumnWidths.For("TokenDateTimeCompositeSearchParam", "Code1");
    private static readonly int RefTokenCode2Width = SearchParamColumnWidths.For("ReferenceTokenCompositeSearchParam", "Code2");

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
            _logger.LogDebug("DetermineCompositeType: {Code} has null or <2 components", searchParam.Code);
            return CompositeType.Unknown;
        }

        var types = searchParam.Component
            .Select(c => c.ResolvedSearchParameter?.Type)
            .ToList();

        _logger.LogDebug(
            "DetermineCompositeType: {Code} has components [{Types}], ResolvedParams=[{Resolved}]",
            searchParam.Code,
            string.Join(", ", types.Select(t => t?.ToString() ?? "null")),
            string.Join(", ", searchParam.Component.Select(c => c.ResolvedSearchParameter?.Code ?? "null")));

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
            systemId1 = await _cache.GetSystemIdAsync(token1.System, cancellationToken);

            if (systemId1 is null)
            {
                return _context.EmptyResourceIds();
            }
        }

        if (!string.IsNullOrEmpty(token2.System))
        {
            systemId2 = await _cache.GetSystemIdAsync(token2.System, cancellationToken);

            if (systemId2 is null)
            {
                return _context.EmptyResourceIds();
            }
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
        var code1 = token1.Code;
        if (!string.IsNullOrEmpty(code1))
        {
            query = code1.Length > TokenTokenCode1Width
                ? query.Where(t => t.CodeOverflow1 != null && t.Code1 + t.CodeOverflow1 == code1)
                : query.Where(t => t.CodeOverflow1 == null && t.Code1 == code1);
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
        var code2 = token2.Code;
        if (!string.IsNullOrEmpty(code2))
        {
            query = code2.Length > TokenTokenCode2Width
                ? query.Where(t => t.CodeOverflow2 != null && t.Code2 + t.CodeOverflow2 == code2)
                : query.Where(t => t.CodeOverflow2 == null && t.Code2 == code2);
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
            systemId1 = await _cache.GetSystemIdAsync(token.System, cancellationToken);

            if (systemId1 is null)
            {
                return _context.EmptyResourceIds();
            }
        }

        // Build base query
        var query = _context.TokenQuantityCompositeSearchParams
            .Where(t => t.SearchParamId == searchParamId);

        if (resourceTypeId.HasValue)
        {
            query = query.Where(t => t.ResourceTypeId == resourceTypeId.Value);
        }

        // Apply first component (token) filter
        var qtyCode = token.Code;
        if (!string.IsNullOrEmpty(qtyCode))
        {
            query = qtyCode.Length > TokenQuantityCode1Width
                ? query.Where(t => t.CodeOverflow1 != null && t.Code1 + t.CodeOverflow1 == qtyCode)
                : query.Where(t => t.CodeOverflow1 == null && t.Code1 == qtyCode);
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
            systemId1 = await _cache.GetSystemIdAsync(token.System, cancellationToken);

            if (systemId1 is null)
            {
                return _context.EmptyResourceIds();
            }
        }

        // Build base query
        var query = _context.TokenStringCompositeSearchParams
            .Where(t => t.SearchParamId == searchParamId);

        if (resourceTypeId.HasValue)
        {
            query = query.Where(t => t.ResourceTypeId == resourceTypeId.Value);
        }

        // Apply first component (token) filter
        var strCode = token.Code;
        if (!string.IsNullOrEmpty(strCode))
        {
            query = strCode.Length > TokenStringCode1Width
                ? query.Where(t => t.CodeOverflow1 != null && t.Code1 + t.CodeOverflow1 == strCode)
                : query.Where(t => t.CodeOverflow1 == null && t.Code1 == strCode);
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
            query = normalizedValue.Length > TokenStringText2Width
                ? query.Where(t => t.TextOverflow2 != null && t.TextOverflow2.StartsWith(normalizedValue))
                : query.Where(t => t.Text2.StartsWith(normalizedValue));
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

        // Detect actual component types from the expressions to handle FHIR spec inconsistencies
        // (e.g., DocumentReference "relationship" parameter has swapped component definitions)
        var comp0IsReference = IsReferenceExpression(component0);
        var comp0IsToken = IsTokenExpression(component0);
        var comp1IsReference = IsReferenceExpression(component1);
        var comp1IsToken = IsTokenExpression(component1);

        // Determine which component is the reference and which is the token
        Expression referenceExpr;
        Expression tokenExpr;

        if (comp0IsReference && comp1IsToken)
        {
            // Expected order: Reference first, Token second
            referenceExpr = component0;
            tokenExpr = component1;
        }
        else if (comp0IsToken && comp1IsReference)
        {
            // Swapped order: Token first, Reference second (e.g., DocumentReference relationship)
            _logger.LogDebug("Detected swapped component order for SearchParamId={SearchParamId}: Token in position 0, Reference in position 1", searchParamId);
            referenceExpr = component1;
            tokenExpr = component0;
        }
        else
        {
            // Fallback to original assumption if we can't determine types
            _logger.LogWarning("Unable to determine component types for Reference|Token composite SearchParamId={SearchParamId}, using assumed order", searchParamId);
            referenceExpr = component0;
            tokenExpr = component1;
        }

        // Extract reference value
        var reference = ExtractReferenceValue(referenceExpr);

        // Extract token value
        var token = ExtractTokenValues(tokenExpr);
        int? systemId2 = null;

        if (!string.IsNullOrEmpty(token.System))
        {
            systemId2 = await _cache.GetSystemIdAsync(token.System, cancellationToken);

            if (systemId2 is null)
            {
                return _context.EmptyResourceIds();
            }
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
        var refTokenCode = token.Code;
        if (!string.IsNullOrEmpty(refTokenCode))
        {
            query = refTokenCode.Length > RefTokenCode2Width
                ? query.Where(r => r.CodeOverflow2 != null && r.Code2 + r.CodeOverflow2 == refTokenCode)
                : query.Where(r => r.CodeOverflow2 == null && r.Code2 == refTokenCode);
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
    /// Determines if an expression contains reference fields.
    /// </summary>
    private bool IsReferenceExpression(Expression expression)
    {
        if (expression is StringExpression stringExpr)
        {
            return stringExpr.FieldName is FieldName.ReferenceResourceType or FieldName.ReferenceResourceId or FieldName.ReferenceBaseUri;
        }

        if (expression is MultiaryExpression multiary)
        {
            return multiary.Expressions.Any(IsReferenceExpression);
        }

        return false;
    }

    /// <summary>
    /// Determines if an expression contains token fields.
    /// </summary>
    private bool IsTokenExpression(Expression expression)
    {
        if (expression is StringExpression stringExpr)
        {
            return stringExpr.FieldName is FieldName.TokenCode or FieldName.TokenSystem or FieldName.TokenText;
        }

        if (expression is MissingFieldExpression missingExpr)
        {
            return missingExpr.FieldName is FieldName.TokenSystem;
        }

        if (expression is MultiaryExpression multiary)
        {
            return multiary.Expressions.Any(IsTokenExpression);
        }

        return false;
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
            systemId1 = await _cache.GetSystemIdAsync(token.System, cancellationToken);

            if (systemId1 is null)
            {
                return _context.EmptyResourceIds();
            }
        }

        // Build base query
        var query = _context.TokenDateTimeCompositeSearchParams
            .Where(t => t.SearchParamId == searchParamId);

        if (resourceTypeId.HasValue)
        {
            query = query.Where(t => t.ResourceTypeId == resourceTypeId.Value);
        }

        // Apply first component (token) filter
        var dtCode = token.Code;
        if (!string.IsNullOrEmpty(dtCode))
        {
            query = dtCode.Length > TokenDateTimeCode1Width
                ? query.Where(t => t.CodeOverflow1 != null && t.Code1 + t.CodeOverflow1 == dtCode)
                : query.Where(t => t.CodeOverflow1 == null && t.Code1 == dtCode);
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
        // The expression builder names the bound each comparator belongs to (QuantityLow/QuantityHigh), so
        // every constraint is applied verbatim to that column rather than inferred from the operator: eq/ap
        // contribute one constraint per bound, the ordering comparators exactly one.
        // eq/ap contribute their two bounds nested in an And, ne nests them in an Or. Descending into every
        // MultiaryExpression indiscriminately flattened the Or's disjuncts into the conjunctive list, so ne
        // ("Low < lower OR High > upper") became "Low < lower AND High > upper" -- unsatisfiable for a point
        // row, inverting ne into "matches nothing". Conjuncts are collected by descending And groups; a
        // disjunction is kept whole for the Union pass below.
        string? quantitySystem = null;
        string? quantityCode = null;
        var conjuncts = new List<(FieldName Field, BinaryOperator Op, decimal Value)>();
        var disjunctions = new List<MultiaryExpression>();

        void ProcessExpression(Expression expr)
        {
            switch (expr)
            {
                case BinaryExpression { FieldName: FieldName.QuantityLow or FieldName.QuantityHigh } binaryExpr:
                    conjuncts.Add((binaryExpr.FieldName, binaryExpr.BinaryOperator, Convert.ToDecimal(binaryExpr.Value)));
                    break;
                case StringExpression { FieldName: FieldName.QuantitySystem } systemExpr:
                    quantitySystem = systemExpr.Value;
                    break;
                case StringExpression { FieldName: FieldName.QuantityCode } codeExpr:
                    quantityCode = codeExpr.Value;
                    break;
                case MultiaryExpression { MultiaryOperation: MultiaryOperator.And } and:
                    foreach (var subExpr in and.Expressions)
                    {
                        ProcessExpression(subExpr);
                    }

                    break;
                case MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } or:
                    disjunctions.Add(or);
                    break;
            }
        }

        ProcessExpression(expression);

        // Apply system filter. A miss means no indexed row carries this system, so the result is empty --
        // it must not fall through leaving the filter unapplied, which would match every system instead.
        if (!string.IsNullOrEmpty(quantitySystem))
        {
            var systemId = await _cache.GetSystemIdAsync(quantitySystem, cancellationToken);
            if (systemId is null)
            {
                return query.Where(_ => false);
            }

            query = query.Where(q => q.SystemId2 == systemId.Value);
        }

        // Apply code filter -- same reasoning as the system filter above.
        if (!string.IsNullOrEmpty(quantityCode))
        {
            var codeId = await _cache.GetQuantityCodeIdAsync(quantityCode, cancellationToken);
            if (codeId is null)
            {
                return query.Where(_ => false);
            }

            query = query.Where(q => q.QuantityCodeId == codeId.Value);
        }

        foreach (var conjunct in conjuncts)
        {
            query = ApplyCompositeQuantityConstraint(query, conjunct.Field, conjunct.Op, conjunct.Value);
        }

        // ne's disjunction: union one branch per disjunct off the narrowed query, then intersect so any
        // co-occurring conjuncts stay in force.
        foreach (var disjunction in disjunctions)
        {
            IQueryable<Entities.TokenQuantityCompositeSearchParamEntity>? branchUnion = null;

            foreach (var disjunct in disjunction.Expressions)
            {
                if (disjunct is not BinaryExpression { FieldName: FieldName.QuantityLow or FieldName.QuantityHigh } binary)
                {
                    throw new NotSupportedException(
                        $"Unexpected disjunct {disjunct.GetType().Name} in a composite Quantity ne search.");
                }

                var branch = ApplyCompositeQuantityConstraint(query, binary.FieldName, binary.BinaryOperator, Convert.ToDecimal(binary.Value));
                branchUnion = branchUnion is null ? branch : branchUnion.Union(branch);
            }

            if (branchUnion is not null)
            {
                query = query.Intersect(branchUnion);
            }
        }

        return query;
    }

    private static IQueryable<Entities.TokenQuantityCompositeSearchParamEntity> ApplyCompositeQuantityConstraint(
        IQueryable<Entities.TokenQuantityCompositeSearchParamEntity> query,
        FieldName field,
        BinaryOperator op,
        decimal value) =>
        (field, op) switch
        {
            (FieldName.QuantityLow, BinaryOperator.GreaterThan) => query.Where(q => q.LowValue > value),
            (FieldName.QuantityLow, BinaryOperator.GreaterThanOrEqual) => query.Where(q => q.LowValue >= value),
            (FieldName.QuantityLow, BinaryOperator.LessThan) => query.Where(q => q.LowValue < value),
            (FieldName.QuantityLow, BinaryOperator.LessThanOrEqual) => query.Where(q => q.LowValue <= value),
            (FieldName.QuantityLow, BinaryOperator.Equal) => query.Where(q => q.LowValue == value),
            (FieldName.QuantityLow, BinaryOperator.NotEqual) => query.Where(q => q.LowValue != value),

            (FieldName.QuantityHigh, BinaryOperator.GreaterThan) => query.Where(q => q.HighValue > value),
            (FieldName.QuantityHigh, BinaryOperator.GreaterThanOrEqual) => query.Where(q => q.HighValue >= value),
            (FieldName.QuantityHigh, BinaryOperator.LessThan) => query.Where(q => q.HighValue < value),
            (FieldName.QuantityHigh, BinaryOperator.LessThanOrEqual) => query.Where(q => q.HighValue <= value),
            (FieldName.QuantityHigh, BinaryOperator.Equal) => query.Where(q => q.HighValue == value),
            (FieldName.QuantityHigh, BinaryOperator.NotEqual) => query.Where(q => q.HighValue != value),

            _ => throw new NotSupportedException(
                $"Composite Quantity search with FieldName {field} and BinaryOperator {op} is not supported."),
        };

    /// <summary>
    /// Builds the date component's predicate from its whole conjunct/disjunct tree, rather than keeping one
    /// bound per field.
    /// <para>
    /// A date comparator can put two bounds on the same field: <c>DateTimeEqualityRewriter</c> opts composites
    /// in and turns <c>eq</c>'s containment shape into <c>Start &gt;= x AND Start &lt;= y AND End &lt;= y</c>,
    /// so a last-writer-wins fold over the tree silently discards <c>Start &gt;= x</c> and leaves a predicate
    /// with no lower bound at all. Every bound is therefore combined, not assigned.
    /// </para>
    /// <para>
    /// <see cref="MultiaryOperator"/> is honoured too: <c>ne</c> lowers to a disjunction, and folding its
    /// operands together with AND asks for a row that is simultaneously before and after the window, which no
    /// row satisfies. An operand the walk does not recognise is unconstrained, so it is dropped from an AND
    /// but collapses the enclosing OR to "no filter" - dropping a disjunct would narrow the result instead.
    /// </para>
    /// </summary>
    private static IQueryable<Entities.TokenDateTimeCompositeSearchParamEntity> ApplyDateTimeFilter(
        IQueryable<Entities.TokenDateTimeCompositeSearchParamEntity> query,
        Expression expression)
    {
        var predicate = BuildDateTimePredicate(expression);

        return predicate is null ? query : query.Where(predicate);
    }

    private static DateTimeCompositePredicate? BuildDateTimePredicate(Expression expression)
    {
        return expression switch
        {
            BinaryExpression binaryExpr => BuildDateTimeBound(binaryExpr),
            MultiaryExpression { MultiaryOperation: MultiaryOperator.And } and1 => CombineConjuncts(and1),
            MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } or1 => CombineDisjuncts(or1),
            _ => null
        };
    }

    private static DateTimeCompositePredicate? CombineConjuncts(MultiaryExpression multiary)
    {
        DateTimeCompositePredicate? combined = null;

        foreach (var subExpr in multiary.Expressions)
        {
            var operand = BuildDateTimePredicate(subExpr);

            if (operand is not null)
            {
                combined = combined is null ? operand : PredicateComposer.And(combined, operand);
            }
        }

        return combined;
    }

    private static DateTimeCompositePredicate? CombineDisjuncts(MultiaryExpression multiary)
    {
        DateTimeCompositePredicate? combined = null;

        foreach (var subExpr in multiary.Expressions)
        {
            var operand = BuildDateTimePredicate(subExpr);

            if (operand is null)
            {
                return null;
            }

            combined = combined is null ? operand : PredicateComposer.Or(combined, operand);
        }

        return combined;
    }

    private static DateTimeCompositePredicate? BuildDateTimeBound(BinaryExpression binaryExpr)
    {
        var dateValue = binaryExpr.Value switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.UtcDateTime,
            _ => default(DateTime?)
        };

        if (!dateValue.HasValue)
        {
            return null;
        }

        // Each arm closes over `value` so EF Core sees a captured variable and parameterizes the comparison,
        // exactly as it did when these were chained Where calls. An inlined constant would bake the date into
        // the SQL text and give every distinct date its own plan-cache entry.
        var value = dateValue.Value;

        return (binaryExpr.FieldName, binaryExpr.BinaryOperator) switch
        {
            (FieldName.DateTimeStart, BinaryOperator.GreaterThanOrEqual) => t => t.StartDateTime2 >= value,
            (FieldName.DateTimeStart, BinaryOperator.GreaterThan) => t => t.StartDateTime2 > value,
            (FieldName.DateTimeStart, BinaryOperator.LessThanOrEqual) => t => t.StartDateTime2 <= value,
            (FieldName.DateTimeStart, BinaryOperator.LessThan) => t => t.StartDateTime2 < value,
            (FieldName.DateTimeStart, BinaryOperator.Equal) => t => t.StartDateTime2 == value,

            (FieldName.DateTimeEnd, BinaryOperator.GreaterThanOrEqual) => t => t.EndDateTime2 >= value,
            (FieldName.DateTimeEnd, BinaryOperator.GreaterThan) => t => t.EndDateTime2 > value,
            (FieldName.DateTimeEnd, BinaryOperator.LessThanOrEqual) => t => t.EndDateTime2 <= value,
            (FieldName.DateTimeEnd, BinaryOperator.LessThan) => t => t.EndDateTime2 < value,
            (FieldName.DateTimeEnd, BinaryOperator.Equal) => t => t.EndDateTime2 == value,

            _ => null
        };
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
