using System.Text;
using DurableTask.Core;
using Microsoft.Extensions.Logging;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Domain;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Serialization;

namespace Ignixa.Application.BackgroundOperations.Export.Activities;

/// <summary>
/// DurableTask activity that searches for a chunk of resources and writes them to NDJSON.
/// Combines search + write into a single atomic operation for streaming export.
/// </summary>
public class SearchAndWriteChunkActivity : AsyncTaskActivity<SearchAndWriteChunkInput, SearchAndWriteChunkOutput>
{
    private readonly ISearchServiceFactory _searchServiceFactory;
    private readonly IBlobStorageClient _blobStorage;
    private readonly ISearchOptionsBuilderFactory _searchOptionsBuilderFactory;
    private readonly ITenantConfigurationStore _tenantConfigurationStore;
    private readonly ILogger<SearchAndWriteChunkActivity> _logger;

    private const int DefaultChunkSize = 1000;

    public SearchAndWriteChunkActivity(
        ISearchServiceFactory searchServiceFactory,
        IBlobStorageClient blobStorage,
        ISearchOptionsBuilderFactory searchOptionsBuilderFactory,
        ITenantConfigurationStore tenantConfigurationStore,
        ILogger<SearchAndWriteChunkActivity> logger)
    {
        _searchServiceFactory = searchServiceFactory ?? throw new ArgumentNullException(nameof(searchServiceFactory));
        _blobStorage = blobStorage ?? throw new ArgumentNullException(nameof(blobStorage));
        _searchOptionsBuilderFactory = searchOptionsBuilderFactory ?? throw new ArgumentNullException(nameof(searchOptionsBuilderFactory));
        _tenantConfigurationStore = tenantConfigurationStore ?? throw new ArgumentNullException(nameof(tenantConfigurationStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task<SearchAndWriteChunkOutput> ExecuteAsync(TaskContext context, SearchAndWriteChunkInput input)
    {
        _logger.LogInformation(
            "Processing chunk for {ResourceType} in tenant {TenantId}, continuation: {Continuation}",
            input.ResourceType,
            input.TenantId,
            input.ContinuationToken ?? "(start)");

        // Get tenant configuration to determine FHIR version
        var tenantConfig = await _tenantConfigurationStore.GetTenantConfigurationAsync(input.TenantId, CancellationToken.None);
        if (tenantConfig == null)
        {
            throw new InvalidOperationException($"Tenant {input.TenantId} not found or inactive");
        }

        var fhirVersion = FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);
        _logger.LogDebug(
            "Using FHIR version {FhirVersion} for tenant {TenantId}",
            fhirVersion,
            input.TenantId);

        var searchService = await _searchServiceFactory.GetSearchServiceAsync(input.TenantId, CancellationToken.None);

        // Stream resources in chunks
        var resources = new List<SearchEntryResult>();
        string? nextContinuation = null;
        int resourceCount = 0;

        try
        {
            // Build SearchOptions with type filter if provided
            // TypeFilter format: "code=http://loinc.org|85354-9&status=final"
            SearchOptions searchOptions;

            if (!string.IsNullOrEmpty(input.TypeFilter))
            {
                _logger.LogInformation(
                    "Applying typeFilter to {ResourceType}: {TypeFilter}",
                    input.ResourceType,
                    input.TypeFilter);

                // Parse the typeFilter query string into QueryParameter objects
                var queryParser = new QueryParameterParser();
                var parameters = queryParser.Parse(input.TypeFilter);

                // Build SearchOptions using the factory with tenant's FHIR version
                var searchOptionsBuilder = _searchOptionsBuilderFactory.Create(fhirVersion);
                searchOptions = searchOptionsBuilder.Build(input.ResourceType, parameters);

                // Override MaxItemCount for chunked export (ignore client's _count parameter)
                searchOptions.MaxItemCount = DefaultChunkSize;
            }
            else
            {
                // No filter - create basic SearchOptions for resource type only
                searchOptions = new SearchOptions
                {
                    ResourceType = input.ResourceType,
                    MaxItemCount = DefaultChunkSize,
                };
            }

            var enumerator = searchService.SearchStreamAsync(searchOptions, CancellationToken.None).GetAsyncEnumerator();

            // If we have a continuation token, we need to skip to that position
            // For now, we'll just enumerate - in production, you'd use the continuation token
            // to resume from the exact position

            while (await enumerator.MoveNextAsync() && resources.Count < DefaultChunkSize)
            {
                resources.Add(enumerator.Current);
                resourceCount++;
            }

            // Check if there are more resources
            // In a real implementation, the search service would provide continuation tokens
            // For now, we'll just check if we got a full chunk
            if (resources.Count >= DefaultChunkSize)
            {
                // Generate a simple continuation token (in production, this would come from the search service)
                nextContinuation = $"chunk_{resourceCount}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search {ResourceType} chunk", input.ResourceType);
            throw;
        }

        if (resources.Count == 0)
        {
            _logger.LogInformation("No more resources found for {ResourceType}", input.ResourceType);
            return new SearchAndWriteChunkOutput(
                ResourceCount: 0,
                ContinuationToken: null,
                FileSizeBytes: 0);
        }

        // Write resources to NDJSON using append mode
        _logger.LogInformation(
            "Writing {Count} {ResourceType} resources to {Path}",
            resources.Count,
            input.ResourceType,
            input.OutputPath);

        // Build NDJSON content (one JSON object per line)
        var ndjsonContent = string.Join("\n",
            resources.Select(r => Encoding.UTF8.GetString(r.ResourceBytes.Span))) + "\n";

        var contentBytes = Encoding.UTF8.GetBytes(ndjsonContent);
        long fileSizeBytes = contentBytes.Length;

        // Append to blob storage
        using var stream = new MemoryStream(contentBytes);
        await _blobStorage.AppendBlobAsync(input.OutputPath, stream, CancellationToken.None);

        _logger.LogInformation(
            "Successfully wrote {Count} resources ({Bytes} bytes) to {Path}, continuation: {Continuation}",
            resources.Count,
            contentBytes.Length,
            input.OutputPath,
            nextContinuation ?? "(none)");

        return new SearchAndWriteChunkOutput(
            ResourceCount: resources.Count,
            ContinuationToken: nextContinuation,
            FileSizeBytes: fileSizeBytes);
    }
}
