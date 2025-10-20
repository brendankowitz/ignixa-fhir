// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.Search.Expressions;

namespace Ignixa.DataLayer.SqlEntityFramework.Search;

/// <summary>
/// Processes chained search expressions using EF Core joins.
/// Handles forward chains (Patient?organization.name=Acme) and reverse chains (_has).
/// </summary>
public class ChainedExpressionProcessor
{
    private readonly FhirDbContext _context;
    private readonly SearchIndexReferenceDataCache _cache;
    private readonly SearchParameterQueryGenerator _parameterQueryGenerator;
    private readonly ILogger<ChainedExpressionProcessor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChainedExpressionProcessor"/> class.
    /// </summary>
    /// <param name="context">The EF Core DbContext.</param>
    /// <param name="cache">The reference data cache.</param>
    /// <param name="parameterQueryGenerator">The parameter query generator for target expressions.</param>
    /// <param name="logger">Logger instance.</param>
    public ChainedExpressionProcessor(
        FhirDbContext context,
        SearchIndexReferenceDataCache cache,
        SearchParameterQueryGenerator parameterQueryGenerator,
        ILogger<ChainedExpressionProcessor> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _parameterQueryGenerator = parameterQueryGenerator ?? throw new ArgumentNullException(nameof(parameterQueryGenerator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Processes a chained expression and returns resource IDs matching the chain criteria.
    /// </summary>
    /// <param name="sourceResourceTypeId">The source resource type ID (e.g., Patient).</param>
    /// <param name="chainedExpression">The chained expression to process.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A queryable of source resource surrogate IDs that match the chain.</returns>
    public async Task<IQueryable<long>> ProcessChainAsync(
        short sourceResourceTypeId,
        ChainedExpression chainedExpression,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(chainedExpression);

        _logger.LogDebug(
            "Processing {ChainType} chain: {SourceType} → {ReferenceParam} → {TargetTypes}",
            chainedExpression.Reversed ? "reverse" : "forward",
            string.Join(",", chainedExpression.ResourceTypes),
            chainedExpression.ReferenceSearchParameter.Code,
            string.Join(",", chainedExpression.TargetResourceTypes));

        if (chainedExpression.Reversed)
        {
            return await ProcessReverseChainAsync(sourceResourceTypeId, chainedExpression, ct);
        }
        else
        {
            return await ProcessForwardChainAsync(sourceResourceTypeId, chainedExpression, ct);
        }
    }

    /// <summary>
    /// Processes a forward chain (e.g., Patient?organization.name=Acme).
    /// Query flow: Source → Reference → Target (filter on target)
    /// </summary>
    private async Task<IQueryable<long>> ProcessForwardChainAsync(
        short sourceResourceTypeId,
        ChainedExpression chainedExpression,
        CancellationToken ct)
    {
        // Step 1: Get target resource type IDs
        var targetResourceTypeIds = await GetResourceTypeIdsAsync(chainedExpression.TargetResourceTypes, ct);
        if (targetResourceTypeIds.Count == 0)
        {
            _logger.LogWarning("Target resource types not found: {TargetTypes}", string.Join(",", chainedExpression.TargetResourceTypes));
            return Enumerable.Empty<long>().AsQueryable();
        }

        // Step 2: Recursively process the target expression to get matching target resource IDs
        IQueryable<long> targetResourceIds;
        if (chainedExpression.Expression is ChainedExpression nestedChain)
        {
            // Nested chain: recursively process
            targetResourceIds = await ProcessChainAsync(targetResourceTypeIds[0], nestedChain, ct);
        }
        else if (chainedExpression.Expression is SearchParameterExpression searchParamExpr)
        {
            // Leaf expression: process search parameter on target
            targetResourceIds = await _parameterQueryGenerator.GenerateQueryAsync(
                targetResourceTypeIds[0],
                searchParamExpr,
                ct);
        }
        else if (chainedExpression.Expression is MultiaryExpression multiaryExpr)
        {
            // Multiple conditions on target: process each and combine
            targetResourceIds = await ProcessMultiaryTargetExpressionAsync(targetResourceTypeIds[0], multiaryExpr, ct);
        }
        else
        {
            _logger.LogWarning("Unsupported chain target expression type: {Type}", chainedExpression.Expression?.GetType().Name);
            return Enumerable.Empty<long>().AsQueryable();
        }

        // Step 3: Find references from source to matching targets
        // Query: Find ReferenceSearchParams where:
        //   - ResourceTypeId = source type (e.g., Patient)
        //   - ReferenceResourceTypeId IN target types (e.g., Organization)
        //   - Join with Resource table to get surrogate ID of referenced resource
        //   - Filter by matching target surrogate IDs
        var referenceQuery = _context.ReferenceSearchParams
            .Where(rsp => rsp.ResourceTypeId == sourceResourceTypeId
                && targetResourceTypeIds.Contains(rsp.ReferenceResourceTypeId ?? 0))
            .Join(_context.Resources,
                rsp => new { ResourceTypeId = rsp.ReferenceResourceTypeId ?? (short)0, ResourceId = rsp.ReferenceResourceId },
                res => new { res.ResourceTypeId, res.ResourceId },
                (rsp, res) => new { rsp.ResourceSurrogateId, TargetSurrogateId = res.ResourceSurrogateId })
            .Where(joined => targetResourceIds.Contains(joined.TargetSurrogateId))
            .Select(joined => joined.ResourceSurrogateId);

        return referenceQuery;
    }

    /// <summary>
    /// Processes a reverse chain (e.g., Patient?_has:Observation:patient:code=1234-5).
    /// Query flow: Target → Reference → Source (filter on source)
    /// </summary>
    private async Task<IQueryable<long>> ProcessReverseChainAsync(
        short sourceResourceTypeId,
        ChainedExpression chainedExpression,
        CancellationToken ct)
    {
        // Reverse chain: find resources that are referenced BY other resources matching criteria
        // Example: Patient?_has:Observation:patient:code=1234-5
        //   Find Patients that are referenced by Observations where code=1234-5

        // Step 1: Get target resource type IDs (the type that references us)
        var targetResourceTypeIds = await GetResourceTypeIdsAsync(chainedExpression.TargetResourceTypes, ct);
        if (targetResourceTypeIds.Count == 0)
        {
            _logger.LogWarning("Target resource types not found: {TargetTypes}", string.Join(",", chainedExpression.TargetResourceTypes));
            return Enumerable.Empty<long>().AsQueryable();
        }

        // Step 2: Process the target expression to get matching target resource IDs
        IQueryable<long> targetResourceIds;
        if (chainedExpression.Expression is SearchParameterExpression searchParamExpr)
        {
            targetResourceIds = await _parameterQueryGenerator.GenerateQueryAsync(
                targetResourceTypeIds[0],
                searchParamExpr,
                ct);
        }
        else if (chainedExpression.Expression is MultiaryExpression multiaryExpr)
        {
            targetResourceIds = await ProcessMultiaryTargetExpressionAsync(targetResourceTypeIds[0], multiaryExpr, ct);
        }
        else
        {
            _logger.LogWarning("Unsupported reverse chain expression type: {Type}", chainedExpression.Expression?.GetType().Name);
            return Enumerable.Empty<long>().AsQueryable();
        }

        // Step 3: Find references FROM matching targets TO source type
        // Query: Find ReferenceSearchParams where:
        //   - ResourceTypeId IN target types (e.g., Observation) - the referencing resource
        //   - ResourceSurrogateId IN (matching target IDs) - the specific referencing resources
        //   - ReferenceResourceTypeId = source type (e.g., Patient) - what they reference
        //   - Join with Resource table to get surrogate ID of the referenced source resource
        var reverseReferenceQuery = _context.ReferenceSearchParams
            .Where(rsp => targetResourceTypeIds.Contains(rsp.ResourceTypeId)
                && targetResourceIds.Contains(rsp.ResourceSurrogateId)
                && rsp.ReferenceResourceTypeId == sourceResourceTypeId)
            .Join(_context.Resources,
                rsp => new { ResourceTypeId = rsp.ReferenceResourceTypeId ?? (short)0, ResourceId = rsp.ReferenceResourceId },
                res => new { res.ResourceTypeId, res.ResourceId },
                (rsp, res) => res.ResourceSurrogateId);

        return reverseReferenceQuery;
    }

    /// <summary>
    /// Processes a multiary expression on the target resource.
    /// </summary>
    private async Task<IQueryable<long>> ProcessMultiaryTargetExpressionAsync(
        short targetResourceTypeId,
        MultiaryExpression multiaryExpr,
        CancellationToken ct)
    {
        var queries = new List<IQueryable<long>>();

        foreach (var subExpr in multiaryExpr.Expressions)
        {
            if (subExpr is SearchParameterExpression searchParamExpr)
            {
                var query = await _parameterQueryGenerator.GenerateQueryAsync(
                    targetResourceTypeId,
                    searchParamExpr,
                    ct);
                queries.Add(query);
            }
        }

        if (queries.Count == 0)
        {
            return Enumerable.Empty<long>().AsQueryable();
        }

        // Combine based on operator
        var result = queries[0];
        for (int i = 1; i < queries.Count; i++)
        {
            result = multiaryExpr.MultiaryOperation == MultiaryOperator.And
                ? result.Intersect(queries[i])
                : result.Union(queries[i]);
        }

        return result;
    }

    /// <summary>
    /// Gets resource type IDs for the given resource type names.
    /// </summary>
    private async Task<List<short>> GetResourceTypeIdsAsync(string[] resourceTypeNames, CancellationToken ct)
    {
        var ids = new List<short>();
        foreach (var typeName in resourceTypeNames)
        {
            var typeId = await _cache.GetResourceTypeIdAsync(typeName);
            if (typeId.HasValue)
            {
                ids.Add(typeId.Value);
            }
        }

        return ids;
    }
}
