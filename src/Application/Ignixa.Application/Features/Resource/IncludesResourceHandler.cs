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
/// This handler re-executes the original match page and returns Include and Outcome entries,
/// supporting independent pagination for _include/_revinclude without discarding warnings.
///
/// Flow:
/// 1. Decode the IncludesContinuationToken to get pagination offset
/// 2. Re-execute the original match page with include resolution
/// 3. Skip Match/probe entries while preserving Outcome entries
/// 4. Apply pagination based on IncludesMaxItemCount
/// 5. Generate new IncludesContinuationToken for further pages
/// </summary>
public class IncludesResourceHandler(
    IPartitionStrategy partitionStrategy,
    IQueryExecutionStrategy executionStrategy,
    IFhirRequestContextAccessor contextAccessor,
    ILogger<IncludesResourceHandler> logger) : IRequestHandler<IncludesResourceQuery, SearchResourcesResult>
{
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

        // Only include pagination is consumed here. Changing the match count or cursor would resolve
        // a different include set, making the include offset skip or duplicate resources.
        // The include window (continuation token and page size) is forwarded: a SQL data layer caps include
        // rows at offset + page size from the start of the ordered include set, and the skip below then
        // drops the first offset of them, as it does for a data layer that returns every include.
        var searchOptionsForIncludes = new SearchOptions(request.SearchOptions)
        {
            // Match the original search's probe exclusion and include-seed boundary.
            ProbeExtraRow = true,
            Total = TotalType.None,
        };

        logger.LogDebug(
            "$includes: Fetching includes starting at offset {Offset} with page size {PageSize}",
            currentOffset,
            pageSize);

        var resourceStream = executionStrategy.SearchStreamAsync(
            partition,
            searchOptionsForIncludes,
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
            if (entry.IsPagingProbe)
            {
                continue;
            }

            if (entry.SearchMode == SearchEntryMode.Outcome)
            {
                yield return entry;
                continue;
            }

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
