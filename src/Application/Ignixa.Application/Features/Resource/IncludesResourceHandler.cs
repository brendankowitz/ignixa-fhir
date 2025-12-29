// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using Medino;
using Microsoft.Extensions.Logging;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;

namespace Ignixa.Application.Features.Resource;

/// <summary>
/// Handler for the $includes operation. Fetches additional included resources from a previous search.
/// This handler re-executes the search but only returns Include entries (not Match entries),
/// supporting independent pagination for _include/_revinclude results.
///
/// Flow:
/// 1. Decode the IncludesContinuationToken to get pagination offset
/// 2. Re-execute the search with include resolution
/// 3. Filter to only Include entries (skip Match entries)
/// 4. Apply pagination based on IncludesMaxItemCount
/// 5. Generate new IncludesContinuationToken for further pages
/// </summary>
public class IncludesResourceHandler(
    IPartitionStrategy partitionStrategy,
    IQueryExecutionStrategy executionStrategy,
    IFhirRequestContextAccessor contextAccessor,
    ILogger<IncludesResourceHandler> logger) : IRequestHandler<IncludesResourceQuery, SearchResourcesResult>
{
    private const int IncludesSearchMultiplier = 10;
    private const int MaxIncludesSearchLimit = 10000;

    public Task<SearchResourcesResult> HandleAsync(
        IncludesResourceQuery request,
        CancellationToken cancellationToken)
    {
        var context = contextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available");

        logger.LogInformation("Fetching includes for {ResourceType} resources ($includes operation)", request.ResourceType);

        var partitionContext = new PartitionResolutionContext
        {
            TenantId = context.TenantId,
            TenantConfiguration = context.TenantConfiguration
        };

        var partition = partitionStrategy.DetermineReadPartition(
            partitionContext,
            request.ResourceType,
            new Dictionary<string, string>());

        logger.LogDebug(
            "$includes: Partition(s) determined: [{PartitionIds}] (Mode: {Mode})",
            string.Join(",", partition.PartitionIds),
            partition.Mode);

        int pageSize = request.SearchOptions.IncludesMaxItemCount ?? request.SearchOptions.MaxItemCount;
        int currentOffset = 0;

        if (!string.IsNullOrWhiteSpace(request.SearchOptions.IncludesContinuationToken))
        {
            if (IncludesContinuationToken.TryDecode(request.SearchOptions.IncludesContinuationToken, out int tokenOffset, out _))
            {
                currentOffset = tokenOffset;
            }
        }

        var searchOptionsWithoutLimit = new SearchOptions
        {
            MaxItemCount = Math.Min(request.SearchOptions.MaxItemCount * IncludesSearchMultiplier, MaxIncludesSearchLimit),
            ContinuationToken = null,
            Expression = request.SearchOptions.Expression,
            Sort = request.SearchOptions.Sort,
            Include = request.SearchOptions.Include,
            RevInclude = request.SearchOptions.RevInclude,
            Total = TotalType.None,
            Summary = request.SearchOptions.Summary,
            UnsupportedParams = request.SearchOptions.UnsupportedParams,
            ResourceType = request.SearchOptions.ResourceType,
            ResourceTypes = request.SearchOptions.ResourceTypes
        };

        logger.LogDebug(
            "$includes: Fetching includes starting at offset {Offset} with page size {PageSize}",
            currentOffset,
            pageSize);

        var resourceStream = executionStrategy.SearchStreamAsync(
            partition,
            searchOptionsWithoutLimit,
            cancellationToken);

        var filteredStream = FilterIncludesWithPaginationAsync(
            resourceStream,
            currentOffset,
            pageSize + 1,
            cancellationToken);

        var result = new SearchResourcesResult(
            Resources: filteredStream,
            Total: null,
            ContinuationToken: null,
            HasMore: false,
            SearchOptions: request.SearchOptions);

        return Task.FromResult(result);
    }

    private static async IAsyncEnumerable<SearchEntryResult> FilterIncludesWithPaginationAsync(
        IAsyncEnumerable<SearchEntryResult> entries,
        int offset,
        int limit,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int skipped = 0;
        int yielded = 0;

        await foreach (var entry in entries.WithCancellation(cancellationToken))
        {
            if (entry.SearchMode != SearchEntryMode.Include)
            {
                continue;
            }

            if (skipped < offset)
            {
                skipped++;
                continue;
            }

            if (yielded >= limit)
            {
                yield break;
            }

            yielded++;
            yield return entry;
        }
    }
}

/// <summary>
/// Helper for encoding/decoding continuation tokens for paginated $includes results.
/// Extends the base ContinuationToken with includes-specific state.
/// </summary>
public static class IncludesContinuationToken
{
    /// <summary>
    /// Encodes pagination state into an includes continuation token.
    /// </summary>
    /// <param name="includesOffset">The offset for include entries (number of includes skipped).</param>
    /// <param name="pageSize">The page size (_includesCount parameter).</param>
    /// <returns>Base64-encoded token string.</returns>
    public static string Encode(int includesOffset, int pageSize)
    {
        var state = new IncludesPaginationState
        {
            IncludesOffset = includesOffset,
            PageSize = pageSize
        };

        string json = System.Text.Json.JsonSerializer.Serialize(state);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes);
    }

    private const int MaxAllowedOffset = 1_000_000;
    private const int MaxAllowedPageSize = 1000;
    private const int MinAllowedPageSize = 1;

    /// <summary>
    /// Decodes an includes continuation token into pagination state.
    /// Includes validation to prevent DoS attacks via malicious token values.
    /// </summary>
    /// <param name="token">The Base64-encoded token string.</param>
    /// <param name="includesOffset">The decoded includes offset value.</param>
    /// <param name="pageSize">The decoded page size value.</param>
    /// <returns>True if decoding succeeded and values are valid, false otherwise.</returns>
    public static bool TryDecode(string token, out int includesOffset, out int pageSize)
    {
        includesOffset = 0;
        pageSize = 10;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            byte[] bytes = Convert.FromBase64String(token);
            string json = System.Text.Encoding.UTF8.GetString(bytes);
            var state = System.Text.Json.JsonSerializer.Deserialize<IncludesPaginationState>(json);

            if (state == null)
            {
                return false;
            }

            if (state.IncludesOffset < 0 || state.IncludesOffset > MaxAllowedOffset)
            {
                return false;
            }

            if (state.PageSize < MinAllowedPageSize || state.PageSize > MaxAllowedPageSize)
            {
                return false;
            }

            includesOffset = state.IncludesOffset;
            pageSize = state.PageSize;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private class IncludesPaginationState
    {
        public int IncludesOffset { get; set; }
        public int PageSize { get; set; }
    }
}
