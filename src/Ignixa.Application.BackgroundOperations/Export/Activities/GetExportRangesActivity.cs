// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Microsoft.Extensions.Logging;
using Ignixa.Application.BackgroundOperations.Export.Models;
using Ignixa.Domain.Abstractions;

namespace Ignixa.Application.BackgroundOperations.Export.Activities;

/// <summary>
/// DurableTask activity that determines surrogate ID ranges for a resource type.
/// Called once per resource type by the coordinator to enable parallel processing.
/// Queries the database to find min/max surrogate IDs, then partitions into equal-sized ranges.
/// </summary>
public class GetExportRangesActivity : AsyncTaskActivity<GetExportRangesInput, GetExportRangesOutput>
{
    private readonly ISearchServiceFactory _searchServiceFactory;
    private readonly ILogger<GetExportRangesActivity> _logger;

    public GetExportRangesActivity(
        ISearchServiceFactory searchServiceFactory,
        ILogger<GetExportRangesActivity> logger)
    {
        _searchServiceFactory = searchServiceFactory ?? throw new ArgumentNullException(nameof(searchServiceFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task<GetExportRangesOutput> ExecuteAsync(
        TaskContext context,
        GetExportRangesInput input)
    {
        _logger.LogInformation(
            "Getting export ranges: TenantId={TenantId}, ResourceType={ResourceType}, NumberOfRanges={NumberOfRanges}",
            input.TenantId,
            input.ResourceType,
            input.NumberOfRanges);

        try
        {
            // Get search service for this tenant
            var searchService = await _searchServiceFactory.GetSearchServiceAsync(
                input.TenantId,
                CancellationToken.None);

            // Query the database to get surrogate ID ranges
            // This partitions the resource type into equal-sized ranges that can be processed independently
            var ranges = await searchService.GetExportRangesAsync(
                input.ResourceType,
                input.NumberOfRanges,
                CancellationToken.None);

            _logger.LogInformation(
                "Determined export ranges: TenantId={TenantId}, ResourceType={ResourceType}, RangeCount={RangeCount}",
                input.TenantId,
                input.ResourceType,
                ranges.Count);

            if (ranges.Count > 0)
            {
                // Log each range for debugging
                for (int i = 0; i < ranges.Count; i++)
                {
                    var (startId, endId) = ranges[i];
                    var rangeSize = endId - startId + 1;
                    _logger.LogDebug(
                        "Range {Index}: [{StartId}..{EndId}] ({Size} resources)",
                        i + 1,
                        startId,
                        endId,
                        rangeSize);
                }
            }
            else
            {
                _logger.LogInformation(
                    "No resources found for export: ResourceType={ResourceType}",
                    input.ResourceType);
            }

            return new GetExportRangesOutput(
                ResourceType: input.ResourceType,
                Ranges: ranges);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to determine export ranges: TenantId={TenantId}, ResourceType={ResourceType}",
                input.TenantId,
                input.ResourceType);

            throw;
        }
    }
}
