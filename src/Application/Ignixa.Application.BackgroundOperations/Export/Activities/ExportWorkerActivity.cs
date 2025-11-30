// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Microsoft.Extensions.Logging;
using Ignixa.Application.BackgroundOperations.Export.Models;
using Ignixa.Domain;
using Ignixa.Domain.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Specification;

namespace Ignixa.Application.BackgroundOperations.Export.Activities;

/// <summary>
/// DurableTask activity that exports a single partition (resource type + surrogate ID range).
/// Streams resources directly from database to file without pagination or buffering the entire result set.
/// Each worker instance runs independently and in parallel with other workers.
/// </summary>
public class ExportWorkerActivity : AsyncTaskActivity<ExportWorkerInput, ExportWorkerOutput>
{
    private readonly ISearchServiceFactory _searchServiceFactory;
    private readonly IExportStreamWriterFactory _writerFactory;
    private readonly ITenantConfigurationStore _tenantConfigurationStore;
    private readonly IQueryParameterParser _parameterParser;
    private readonly ISearchOptionsBuilder _searchOptionsBuilder;
    private readonly ILogger<ExportWorkerActivity> _logger;

    public ExportWorkerActivity(
        ISearchServiceFactory searchServiceFactory,
        IExportStreamWriterFactory writerFactory,
        ITenantConfigurationStore tenantConfigurationStore,
        IQueryParameterParser parameterParser,
        ISearchOptionsBuilder searchOptionsBuilder,
        ILogger<ExportWorkerActivity> logger)
    {
        _searchServiceFactory = searchServiceFactory ?? throw new ArgumentNullException(nameof(searchServiceFactory));
        _writerFactory = writerFactory ?? throw new ArgumentNullException(nameof(writerFactory));
        _tenantConfigurationStore = tenantConfigurationStore ?? throw new ArgumentNullException(nameof(tenantConfigurationStore));
        _parameterParser = parameterParser ?? throw new ArgumentNullException(nameof(parameterParser));
        _searchOptionsBuilder = searchOptionsBuilder ?? throw new ArgumentNullException(nameof(searchOptionsBuilder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task<ExportWorkerOutput> ExecuteAsync(
        TaskContext context,
        ExportWorkerInput input)
    {
        _logger.LogInformation(
            "Starting export worker: Job={JobId}, Type={ResourceType}, Range=[{StartId}..{EndId}]",
            input.JobId,
            input.ResourceType,
            input.StartSurrogateId,
            input.EndSurrogateId);

        try
        {
            // Get tenant configuration to determine FHIR version
            var tenantConfig = await _tenantConfigurationStore.GetTenantConfigurationAsync(
                input.TenantId,
                CancellationToken.None);

            if (tenantConfig == null)
            {
                throw new InvalidOperationException($"Tenant {input.TenantId} not found or inactive");
            }

            // Get search service for this tenant
            var searchService = await _searchServiceFactory.GetSearchServiceAsync(
                input.TenantId,
                CancellationToken.None);

            // Create streaming writer (writes to blob storage as we process)
            await using var writer = await _writerFactory.CreateAsync(
                input.TenantId,
                input.OutputPath,
                CancellationToken.None);

            long resourcesExported = 0;

            try
            {
                // Build search query string with all applicable filters
                var queryStringBuilder = new System.Text.StringBuilder();

                // Add type-specific filters from TypeFilters dictionary
                if (input.TypeFilters?.ContainsKey(input.ResourceType) == true)
                {
                    queryStringBuilder.Append(input.TypeFilters[input.ResourceType]);
                }

                // Add _since parameter for temporal filtering (FHIR spec: resources modified since this time)
                if (input.Since.HasValue)
                {
                    if (queryStringBuilder.Length > 0)
                    {
                        queryStringBuilder.Append('&');
                    }
                    queryStringBuilder.Append("_since=");
                    queryStringBuilder.Append(Uri.EscapeDataString(input.Since.Value.ToString("O")));
                }

                // Parse all filter parameters and build SearchOptions with expression
                var filterParams = _parameterParser.Parse(queryStringBuilder.ToString());

                // Use SearchOptionsBuilder to properly parse filters into expressions
                var searchOptions = _searchOptionsBuilder.Build(input.ResourceType, filterParams);

                // Add partition boundaries (surrogate ID range)
                searchOptions.MaxItemCount = 50_000;  // Large batches (memory-safe due to streaming)
                searchOptions.StartSurrogateId = input.StartSurrogateId;
                searchOptions.EndSurrogateId = input.EndSurrogateId;

                // Log filter application
                if (filterParams.Count > 0)
                {
                    _logger.LogDebug(
                        "Export worker applying filters: Job={JobId}, Type={ResourceType}, Filters={FilterCount}",
                        input.JobId,
                        input.ResourceType,
                        filterParams.Count);
                }

                // Stream results directly from database to file
                // This single async enumeration runs until ALL resources in the range are processed
                // No continuation tokens, no checkpoints - just stream until done
                await foreach (var resource in searchService.SearchStreamAsync(searchOptions, CancellationToken.None))
                {
                    // Write resource directly to file stream
                    // (buffered internally by IExportStreamWriter, then flushed periodically)
                    await writer.WriteResourceAsync(resource, CancellationToken.None);
                    resourcesExported++;

                    // Log progress periodically (every 10K resources)
                    if (resourcesExported % 10_000 == 0)
                    {
                        _logger.LogInformation(
                            "Export progress: Job={JobId}, Type={ResourceType}, Range=[{StartId}..{EndId}], Count={Count}",
                            input.JobId,
                            input.ResourceType,
                            input.StartSurrogateId,
                            input.EndSurrogateId,
                            resourcesExported);
                    }
                }

                // Final flush ensures all remaining data is written to blob storage
                await writer.FlushAsync(CancellationToken.None);

                _logger.LogInformation(
                    "Completed export worker: Job={JobId}, Type={ResourceType}, Range=[{StartId}..{EndId}], Count={Count}, Bytes={Bytes}",
                    input.JobId,
                    input.ResourceType,
                    input.StartSurrogateId,
                    input.EndSurrogateId,
                    resourcesExported,
                    writer.BytesWritten);

                return new ExportWorkerOutput(
                    ResourceType: input.ResourceType,
                    StartSurrogateId: input.StartSurrogateId,
                    EndSurrogateId: input.EndSurrogateId,
                    ResourcesExported: resourcesExported,
                    BytesWritten: writer.BytesWritten);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Export worker failed during processing: Job={JobId}, Type={ResourceType}, Range=[{StartId}..{EndId}]",
                    input.JobId,
                    input.ResourceType,
                    input.StartSurrogateId,
                    input.EndSurrogateId);

                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Export worker failed: Job={JobId}, Type={ResourceType}, Range=[{StartId}..{EndId}]",
                input.JobId,
                input.ResourceType,
                input.StartSurrogateId,
                input.EndSurrogateId);

            throw;
        }
    }
}
