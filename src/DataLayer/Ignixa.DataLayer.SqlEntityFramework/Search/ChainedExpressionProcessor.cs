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
    /// <param name="sourceResourceTypeId">The source resource type ID (e.g., Patient), or null for system-wide search.</param>
    /// <param name="chainedExpression">The chained expression to process.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A queryable of source resource surrogate IDs that match the chain.</returns>
    public async Task<IQueryable<long>> ProcessChainAsync(
        short? sourceResourceTypeId,
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
        short? sourceResourceTypeId,
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

        // Step 2.5: Get the SearchParamId for the reference search parameter
        // This ensures we only match references using the specific parameter requested in the chain
        var refSearchParamId = await _cache.GetSearchParamIdAsync(chainedExpression.ReferenceSearchParameter);
        if (!refSearchParamId.HasValue)
        {
            _logger.LogWarning(
                "Reference search parameter not found for forward chain: {Uri}",
                chainedExpression.ReferenceSearchParameter.Url);
            return Enumerable.Empty<long>().AsQueryable();
        }

        _logger.LogDebug(
            "Forward chain query params: SourceTypeId={SourceTypeId}, RefSearchParamId={RefSearchParamId}, RefParamCode={RefParamCode}, TargetTypeIds=[{TargetTypeIds}]",
            sourceResourceTypeId,
            refSearchParamId.Value,
            chainedExpression.ReferenceSearchParameter.Code,
            string.Join(",", targetResourceTypeIds));

        // Step 3: Find references from source to matching targets
        // Query: Find ReferenceSearchParams where:
        //   - ResourceTypeId = source type (e.g., Patient), or null for system-wide search
        //   - ReferenceResourceTypeId IN target types (e.g., Organization)
        //   - SearchParamId = reference parameter (e.g., organization) - the specific reference parameter
        //   - Join with Resource table to get surrogate ID of referenced resource
        //   - Filter by matching target surrogate IDs (using subquery, not materialized list)
        //   - IMPORTANT: Only join with current (non-history, non-deleted) resources
        var referenceResults = _context.ReferenceSearchParams
            .Where(rsp => (!sourceResourceTypeId.HasValue || rsp.ResourceTypeId == sourceResourceTypeId.Value)
                && EF.Constant(targetResourceTypeIds).Contains(rsp.ReferenceResourceTypeId ?? 0)
                && rsp.SearchParamId == refSearchParamId.Value)
            .Join(
                _context.Resources.Where(r => !r.IsHistory && !r.IsDeleted),
                rsp => new { ResourceTypeId = rsp.ReferenceResourceTypeId ?? (short)0, ResourceId = rsp.ReferenceResourceId },
                res => new { res.ResourceTypeId, res.ResourceId },
                (rsp, res) => new { rsp.ResourceSurrogateId, TargetSurrogateId = res.ResourceSurrogateId })
            .Where(joined => targetResourceIds.Contains(joined.TargetSurrogateId))
            .Select(joined => joined.ResourceSurrogateId);

        return referenceResults;
    }

    /// <summary>
    /// Processes a reverse chain (e.g., Patient?_has:Observation:patient:code=1234-5).
    /// Query flow: Target → Reference → Source (filter on source)
    /// </summary>
    private async Task<IQueryable<long>> ProcessReverseChainAsync(
        short? sourceResourceTypeId,
        ChainedExpression chainedExpression,
        CancellationToken ct)
    {
        // Reverse chain: find resources that are referenced BY other resources matching criteria
        // Example: Patient?_has:Observation:patient:code=1234-5
        //   Find Patients that are referenced by Observations where code=1234-5

        // Step 1: Get referencing resource type IDs (the type that references us)
        // For reverse chains: chainedExpression.ResourceTypes contains the referencing type (e.g., Observation)
        //                     chainedExpression.TargetResourceTypes contains the referenced type (e.g., Patient)
        var referencingResourceTypeIds = await GetResourceTypeIdsAsync(chainedExpression.ResourceTypes, ct);
        if (referencingResourceTypeIds.Count == 0)
        {
            _logger.LogWarning("Referencing resource types not found: {ReferencingTypes}", string.Join(",", chainedExpression.ResourceTypes));
            return Enumerable.Empty<long>().AsQueryable();
        }

        // Step 2: Process the expression to get matching referencing resource IDs
        // These are Observations (the referencing type) that match the criteria (e.g., code=527)
        IQueryable<long> referencingResourceIds;
        if (chainedExpression.Expression is SearchParameterExpression searchParamExpr)
        {
            referencingResourceIds = await _parameterQueryGenerator.GenerateQueryAsync(
                referencingResourceTypeIds[0],
                searchParamExpr,
                ct);
        }
        else if (chainedExpression.Expression is MultiaryExpression multiaryExpr)
        {
            referencingResourceIds = await ProcessMultiaryTargetExpressionAsync(referencingResourceTypeIds[0], multiaryExpr, ct);
        }
        else if (chainedExpression.Expression is ChainedExpression nestedChain)
        {
            // Nested chain: recursively process
            referencingResourceIds = await ProcessChainAsync(referencingResourceTypeIds[0], nestedChain, ct);
        }
        else
        {
            _logger.LogWarning("Unsupported reverse chain expression type: {Type}", chainedExpression.Expression?.GetType().Name);
            return Enumerable.Empty<long>().AsQueryable();
        }

        // Step 2.5: Get the SearchParamId for the reference search parameter
        // This ensures we only match references using the specific parameter requested in the _has query
        var refSearchParamId = await _cache.GetSearchParamIdAsync(chainedExpression.ReferenceSearchParameter);
        if (!refSearchParamId.HasValue)
        {
            _logger.LogWarning(
                "Reference search parameter not found for reverse chain: {Uri}",
                chainedExpression.ReferenceSearchParameter.Url);
            return Enumerable.Empty<long>().AsQueryable();
        }

        // Step 3: Find references FROM matching referencing resources TO source type
        // Query: Find ReferenceSearchParams where:
        //   - ResourceTypeId IN referencing types (e.g., Observation) - the referencing resource
        //   - ResourceSurrogateId IN (matching referencing IDs) - the specific referencing resources
        //   - ReferenceResourceTypeId = source type (e.g., Patient) - what they reference
        //   - SearchParamId = reference parameter (e.g., clinical-patient) - the specific reference parameter
        //   - Join with Resource table to get surrogate ID of the referenced source resource
        //   - IMPORTANT: Only join with current (non-history, non-deleted) resources
        var reverseReferenceQuery = _context.ReferenceSearchParams
            .Where(rsp => EF.Constant(referencingResourceTypeIds).Contains(rsp.ResourceTypeId)
                && referencingResourceIds.Contains(rsp.ResourceSurrogateId)
                && (!sourceResourceTypeId.HasValue || rsp.ReferenceResourceTypeId == sourceResourceTypeId.Value)
                && rsp.SearchParamId == refSearchParamId.Value)
            .Join(
                _context.Resources.Where(r => !r.IsHistory && !r.IsDeleted),
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
