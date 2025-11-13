// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;
using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.Export.Models;
using Ignixa.Application.Features.Search;
using Ignixa.DataLayer.BlobStorage;
using Ignixa.Domain;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Serialization;
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
    private readonly ViewDefinitionLoader _viewDefinitionLoader;
    private readonly IBlobStorageClient _blobStorageClient;
    private readonly IFhirVersionContext _fhirVersionContext;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ExportWorkerActivity> _logger;

    public ExportWorkerActivity(
        ISearchServiceFactory searchServiceFactory,
        IExportStreamWriterFactory writerFactory,
        ITenantConfigurationStore tenantConfigurationStore,
        IQueryParameterParser parameterParser,
        ISearchOptionsBuilder searchOptionsBuilder,
        ViewDefinitionLoader viewDefinitionLoader,
        IBlobStorageClient blobStorageClient,
        IFhirVersionContext fhirVersionContext,
        ILoggerFactory loggerFactory,
        ILogger<ExportWorkerActivity> logger)
    {
        _searchServiceFactory = searchServiceFactory ?? throw new ArgumentNullException(nameof(searchServiceFactory));
        _writerFactory = writerFactory ?? throw new ArgumentNullException(nameof(writerFactory));
        _tenantConfigurationStore = tenantConfigurationStore ?? throw new ArgumentNullException(nameof(tenantConfigurationStore));
        _parameterParser = parameterParser ?? throw new ArgumentNullException(nameof(parameterParser));
        _searchOptionsBuilder = searchOptionsBuilder ?? throw new ArgumentNullException(nameof(searchOptionsBuilder));
        _viewDefinitionLoader = viewDefinitionLoader ?? throw new ArgumentNullException(nameof(viewDefinitionLoader));
        _blobStorageClient = blobStorageClient ?? throw new ArgumentNullException(nameof(blobStorageClient));
        _fhirVersionContext = fhirVersionContext ?? throw new ArgumentNullException(nameof(fhirVersionContext));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
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
            IExportStreamWriter writer;

            if (!string.IsNullOrEmpty(input.ViewDefinitionId))
            {
                // ViewDefinition-guided export (Parquet with transformation)
                _logger.LogInformation(
                    "Using ViewDefinition export: Job={JobId}, ViewDefinitionId={ViewDefinitionId}, OutputPath={OutputPath}",
                    input.JobId,
                    input.ViewDefinitionId,
                    input.OutputPath);

                // Load ViewDefinition from datastore
                var viewDefNode = await _viewDefinitionLoader.LoadViewDefinitionAsync(
                    input.TenantId,
                    input.ViewDefinitionId,
                    CancellationToken.None);

                // Get structure provider for tenant's FHIR version
                var fhirVersion = FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);
                var structureProvider = _fhirVersionContext.GetSchemaProvider(fhirVersion, input.TenantId);

                // Create ViewDefinition export writer with schema derived from ViewDefinition
                // This constructor builds the Parquet schema from ViewDefinition columns
#pragma warning disable CA2000 // Dispose ownership transferred to 'await using' statement
                writer = new ViewDefinitionExportStreamWriter(
                    _blobStorageClient,
                    input.OutputPath,
                    viewDefNode,
                    structureProvider,
                    _loggerFactory);
#pragma warning restore CA2000
            }
            else
            {
                // Standard export (NDJSON or raw Parquet, depending on factory configuration)
                writer = await _writerFactory.CreateAsync(
                    input.TenantId,
                    input.OutputPath,
                    CancellationToken.None);
            }

            long resourcesExported = 0;

            await using (writer)
            {
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

                    // If GroupId is specified, add _id filter to only export resources for Group members
                    if (!string.IsNullOrEmpty(input.GroupId) && input.ResourceType == "Patient")
                    {
                        // Use search service to resolve Group members
                        // Query: GET /Group/{id}?_include=Group:member&_include:iterate=Group:member
                        // This resolves all members including nested groups
                        var groupQueryString = $"_id={Uri.EscapeDataString(input.GroupId)}&_include=Group:member&_include:iterate=Group:member";
                        var groupFilterParams = _parameterParser.Parse(groupQueryString);
                        var groupSearchOptions = _searchOptionsBuilder.Build("Group", groupFilterParams);

                        var patientIds = new List<string>();
                        await foreach (var resource in searchService.SearchStreamAsync(groupSearchOptions, CancellationToken.None))
                        {
                            if (resource.ResourceType == "Patient")
                            {
                                patientIds.Add(resource.ResourceId);
                            }
                        }

                        if (patientIds.Count == 0)
                        {
                            _logger.LogWarning("Group {GroupId} has no Patient members, export will be empty", input.GroupId);
                            return new ExportWorkerOutput(input.ResourceType, input.StartSurrogateId, input.EndSurrogateId, 0, 0);
                        }

                        // Add _id filter for the patient IDs
                        var idFilter = "_id=" + string.Join(",", patientIds.Select(Uri.EscapeDataString));
                        if (queryStringBuilder.Length > 0)
                        {
                            queryStringBuilder.Append('&');
                        }
                        queryStringBuilder.Append(idFilter);

                        _logger.LogInformation("Group export: Resolved {Count} patient members from Group {GroupId}", patientIds.Count, input.GroupId);
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

                    // Producer-consumer pattern: decouple database reading from writing/ViewDefinition evaluation
                    // Producer reads from search service and writes to channel
                    // Consumer(s) read from channel, evaluate ViewDefinition, and write to file sequentially
                    var channel = Channel.CreateBounded<SearchEntryResult>(
                        new BoundedChannelOptions(capacity: 500)
                        {
                            FullMode = BoundedChannelFullMode.Wait
                        });

                    // Producer task: Read from search stream and write to channel
                    var producerTask = Task.Run(async () =>
                    {
                        try
                        {
                            await foreach (var resource in searchService.SearchStreamAsync(searchOptions, CancellationToken.None))
                            {
                                await channel.Writer.WriteAsync(resource, CancellationToken.None);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "Producer task failed: Job={JobId}, Type={ResourceType}",
                                input.JobId,
                                input.ResourceType);
                            throw;
                        }
                        finally
                        {
                            channel.Writer.Complete();
                        }
                    });

                    // Consumer: Process from channel, evaluate ViewDefinition, write sequentially
                    try
                    {
                        await foreach (var resource in channel.Reader.ReadAllAsync(CancellationToken.None))
                        {
                            // Write resource directly to file stream
                            // (buffered internally by IExportStreamWriter, then flushed periodically)
                            // ViewDefinition evaluation happens inside WriteResourceAsync
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
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Consumer task failed: Job={JobId}, Type={ResourceType}",
                            input.JobId,
                            input.ResourceType);
                        throw;
                    }

                    // Wait for producer to complete (ensures all errors are propagated)
                    await producerTask;

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
