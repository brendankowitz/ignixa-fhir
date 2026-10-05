// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Export.Models;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.DataLayer.BlobStorage;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Serialization;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.Export.Activities;

/// <summary>
/// DurableTask activity that exports a single partition (resource type + surrogate ID range).
/// Exhausts the partition in bounded pages without buffering the entire result set.
/// Each worker instance runs independently and in parallel with other workers.
/// </summary>
public class ExportWorkerActivity : AsyncTaskActivity<ExportWorkerInput, ExportWorkerOutput>
{
    private const int PageSize = 1000;
    private readonly ISearchServiceFactory _searchServiceFactory;
    private readonly IExportStreamWriterFactory _writerFactory;
    private readonly ITenantConfigurationStore _tenantConfigurationStore;
    private readonly IQueryParameterParser _parameterParser;
    private readonly ISearchOptionsBuilderFactory _searchOptionsBuilderFactory;
    private readonly ViewDefinitionLoader _viewDefinitionLoader;
    private readonly IBlobStorageClient _blobStorageClient;
    private readonly IFhirVersionContext _fhirVersionContext;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ExportWorkerActivity> _logger;
    private readonly ExportGroupResolver _groupResolver;
    private readonly IFhirRequestContextAccessor _fhirContextAccessor;

    public ExportWorkerActivity(
        ISearchServiceFactory searchServiceFactory,
        IExportStreamWriterFactory writerFactory,
        ITenantConfigurationStore tenantConfigurationStore,
        IQueryParameterParser parameterParser,
        ISearchOptionsBuilderFactory searchOptionsBuilderFactory,
        ViewDefinitionLoader viewDefinitionLoader,
        IBlobStorageClient blobStorageClient,
        IFhirVersionContext fhirVersionContext,
        ILoggerFactory loggerFactory,
        ILogger<ExportWorkerActivity> logger,
        ExportGroupResolver groupResolver,
        IFhirRequestContextAccessor fhirContextAccessor)
    {
        _searchServiceFactory = searchServiceFactory ?? throw new ArgumentNullException(nameof(searchServiceFactory));
        _writerFactory = writerFactory ?? throw new ArgumentNullException(nameof(writerFactory));
        _tenantConfigurationStore = tenantConfigurationStore ?? throw new ArgumentNullException(nameof(tenantConfigurationStore));
        _parameterParser = parameterParser ?? throw new ArgumentNullException(nameof(parameterParser));
        _searchOptionsBuilderFactory = searchOptionsBuilderFactory ?? throw new ArgumentNullException(nameof(searchOptionsBuilderFactory));
        _viewDefinitionLoader = viewDefinitionLoader ?? throw new ArgumentNullException(nameof(viewDefinitionLoader));
        _blobStorageClient = blobStorageClient ?? throw new ArgumentNullException(nameof(blobStorageClient));
        _fhirVersionContext = fhirVersionContext ?? throw new ArgumentNullException(nameof(fhirVersionContext));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _groupResolver = groupResolver ?? throw new ArgumentNullException(nameof(groupResolver));
        _fhirContextAccessor = fhirContextAccessor ?? throw new ArgumentNullException(nameof(fhirContextAccessor));
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

        var previousContext = _fhirContextAccessor.RequestContext;
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

            var version = FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);
            _fhirContextAccessor.RequestContext = FhirRequestContextFactory.CreateBackgroundContext(
                input.TenantId, tenantConfig, version, input.ResourceType);

            // Get search service for this tenant
            var searchService = await _searchServiceFactory.GetSearchServiceAsync(
                input.TenantId,
                CancellationToken.None);
            var schema = _fhirVersionContext.GetSchemaProvider(version, input.TenantId);
            var searchOptionsBuilder = _searchOptionsBuilderFactory.Create(version, input.TenantId);

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

                // Create ViewDefinition export writer with schema derived from ViewDefinition
                // This constructor builds the Parquet schema from ViewDefinition columns
#pragma warning disable CA2000 // Dispose ownership transferred to 'await using' statement
                writer = new ViewDefinitionExportStreamWriter(
                    _blobStorageClient,
                    input.OutputPath,
                    viewDefNode,
                    schema,
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

                    // Bulk _since is not a search parameter; translate it to the inclusive timestamp predicate.
                    if (input.Since.HasValue)
                    {
                        if (queryStringBuilder.Length > 0)
                        {
                            queryStringBuilder.Append('&');
                        }
                        queryStringBuilder.Append("_lastUpdated=ge");
                        queryStringBuilder.Append(Uri.EscapeDataString(input.Since.Value.ToString("O", CultureInfo.InvariantCulture)));
                    }

                    Expression? groupExpression = null;
                    if (!string.IsNullOrEmpty(input.GroupId))
                    {
                        var patientIds = await _groupResolver.ResolvePatientIdsAsync(
                            input.TenantId, input.GroupId, schema, CancellationToken.None);

                        if (patientIds.Count == 0)
                        {
                            _logger.LogWarning("Group {GroupId} has no Patient members, export will be empty", input.GroupId);
                            return new ExportWorkerOutput(input.ResourceType, input.StartSurrogateId, input.EndSurrogateId, 0, 0);
                        }

                        if (input.ResourceType == "Patient")
                        {
                            if (queryStringBuilder.Length > 0)
                            {
                                queryStringBuilder.Append('&');
                            }
                            queryStringBuilder.Append("_id=" + string.Join(",", patientIds.Select(Uri.EscapeDataString)));
                        }
                        else
                        {
                            groupExpression = Expression.Or(patientIds.Select(id =>
                                (Expression)new CompartmentSearchExpression(
                                    "Patient", id, new HashSet<string>(StringComparer.Ordinal) { input.ResourceType })).ToArray());
                        }

                        _logger.LogInformation("Group export: Resolved {Count} patient members from Group {GroupId}", patientIds.Count, input.GroupId);
                    }

                    // Parse all filter parameters and build SearchOptions with expression
                    var filterParams = _parameterParser.Parse(queryStringBuilder.ToString());

                    // Use SearchOptionsBuilder to properly parse filters into expressions
                    var searchOptions = searchOptionsBuilder.Build(input.ResourceType, filterParams);
                    SearchModifierNotSupportedException.ThrowIfAny(searchOptions);
                    if (groupExpression is not null)
                    {
                        searchOptions.Expression = searchOptions.Expression switch
                        {
                            null => groupExpression,
                            MultiaryExpression { MultiaryOperation: MultiaryOperator.And } and =>
                                Expression.And([groupExpression, .. and.Expressions]),
                            var other => Expression.And(groupExpression, other)
                        };
                    }

                    // Add partition boundaries (surrogate ID range)
                    searchOptions.MaxItemCount = PageSize;
                    // Bulk output is unordered; use the provider's stable traversal order for continuation.
                    searchOptions.Sort = [];
                    searchOptions.ProbeExtraRow = true;
                    searchOptions.UseExportContinuation = true;
                    searchOptions.ContinuationToken = null;
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

                    while (true)
                    {
                        string? nextPage = null;
                        await foreach (var resource in searchService.SearchStreamAsync(searchOptions, CancellationToken.None))
                        {
                            if (resource.IsPagingProbe)
                            {
                                nextPage = resource.ContinuationToken;
                                if (string.IsNullOrEmpty(nextPage))
                                {
                                    throw new InvalidOperationException("The export provider returned a paging probe without a continuation.");
                                }
                                continue;
                            }
                            if (resource.SearchMode != SearchEntryMode.Match)
                            {
                                throw new InvalidOperationException("Export searches must return only matching resources, not includes or paging probes.");
                            }

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

                        if (nextPage is null)
                        {
                            break;
                        }

                        if (nextPage == searchOptions.ContinuationToken)
                        {
                            throw new InvalidOperationException("The export provider returned a non-advancing continuation.");
                        }
                        searchOptions.ContinuationToken = nextPage;
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
                finally
                {
                    _fhirContextAccessor.RequestContext = previousContext;
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
