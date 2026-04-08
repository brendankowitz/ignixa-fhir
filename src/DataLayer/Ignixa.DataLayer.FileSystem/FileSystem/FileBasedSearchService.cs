// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using Ignixa.Abstractions;
using Microsoft.Extensions.Logging;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.InMemory;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;

namespace Ignixa.DataLayer.FileSystem.FileSystem;

/// <summary>
/// File-based implementation of search service.
/// Phase 1.2: In-memory filtering using SearchQueryInterpreter.
/// Loads metadata with search indices, applies predicate, then loads matching resources.
/// </summary>
public partial class FileBasedSearchService : ISearchService
{
    private readonly FileBasedFhirRepository _repository;
    private readonly ILogger<FileBasedSearchService> _logger;
    private readonly string _baseDirectory;
    private readonly SearchQueryInterpreter _searchQueryInterpreter;

    public FileBasedSearchService(
        IFhirRepository repository,
        ILogger<FileBasedSearchService> logger,
        string baseDirectory)
    {
        _repository = (repository as FileBasedFhirRepository) ?? throw new ArgumentException(
            "FileBasedSearchService requires FileBasedFhirRepository",
            nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _baseDirectory = baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory));
        _searchQueryInterpreter = new SearchQueryInterpreter();
    }

    public async ValueTask<IReadOnlyList<SearchEntryResult>> SearchAsync<TSearchOptions>(
        TSearchOptions searchOptions,
        CancellationToken ct = default)
        where TSearchOptions : class
    {
        if (searchOptions is not SearchOptions options)
        {
            throw new ArgumentException($"Expected SearchOptions, got {typeof(TSearchOptions).Name}", nameof(searchOptions));
        }

        LogSearching(_logger, options.ResourceType, options.Expression != null);

        var resourceType = options.ResourceType;

        // Step 1: Load metadata with search indices (lightweight - no resource JSON loading)
        var allMetadata = await _repository.GetResourceMetadataAsync(resourceType, ct);

        if (allMetadata.Count == 0)
        {
            LogNoResourcesFound(_logger, resourceType);
            return Array.Empty<SearchEntryResult>();
        }

        LogLoadedMetadata(_logger, allMetadata.Count, resourceType);

        // Step 2: Apply search expression filter if provided
        IEnumerable<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> filteredMetadata = allMetadata;

        if (options.Expression != null)
        {
            LogApplyingSearchFilter(_logger);

            // Convert expression to predicate using SearchQueryInterpreter
            var predicate = options.Expression.AcceptVisitor(_searchQueryInterpreter, default);

            // Apply predicate to filter metadata
            filteredMetadata = predicate(allMetadata);

            int filteredCount = filteredMetadata.Count();
            LogSearchFilterResults(_logger, allMetadata.Count, filteredCount);
        }

        // Step 2.5: Apply surrogate ID range filtering for export partitioning
        // For file-based storage, we use index position as the "surrogate ID"
        if (options.StartSurrogateId.HasValue && options.EndSurrogateId.HasValue)
        {
            var filteredList = filteredMetadata.ToList();
            filteredMetadata = filteredList
                .Skip((int)options.StartSurrogateId.Value)
                .Take((int)(options.EndSurrogateId.Value - options.StartSurrogateId.Value + 1));

            LogSurrogateIdRangeFilter(_logger, options.StartSurrogateId.Value, options.EndSurrogateId.Value);
        }

        // Step 3: Apply pagination
        int skip = 0; // TODO: Parse continuation token
        int take = options.MaxItemCount;

        var pagedKeys = filteredMetadata
            .Skip(skip)
            .Take(take)
            .Select(m => m.Location)
            .ToList();

        LogPagination(_logger, skip, take, pagedKeys.Count);

        // Step 4: Load ONLY the matching resources (not all resources)
        var results = new List<SearchEntryResult>();
        foreach (var key in pagedKeys)
        {
            var resource = await _repository.GetAsync(key, ct);
            if (resource != null)
            {
                results.Add(resource);
            }
        }

        int totalMatching = filteredMetadata.Count();
        LogSearchResults(_logger, results.Count, totalMatching, take);

        return results;
    }

    public async IAsyncEnumerable<SearchEntryResult> SearchStreamAsync<TSearchOptions>(
        TSearchOptions searchOptions,
        [EnumeratorCancellation] CancellationToken ct = default)
        where TSearchOptions : class
    {
        if (searchOptions is not SearchOptions options)
        {
            throw new ArgumentException($"Expected SearchOptions, got {typeof(TSearchOptions).Name}", nameof(searchOptions));
        }

        LogStreamingSearch(_logger, options.ResourceType, options.Expression != null);

        var resourceType = options.ResourceType;

        // Step 1: Load metadata with search indices (lightweight - no resource JSON loading)
        var allMetadata = await _repository.GetResourceMetadataAsync(resourceType, ct);

        if (allMetadata.Count == 0)
        {
            LogNoResourcesFound(_logger, resourceType);
            yield break;
        }

        LogLoadedMetadata(_logger, allMetadata.Count, resourceType);

        // Step 2: Apply search expression filter if provided
        IEnumerable<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> filteredMetadata = allMetadata;

        if (options.Expression != null)
        {
            LogApplyingSearchFilter(_logger);

            // Convert expression to predicate using SearchQueryInterpreter
            var predicate = options.Expression.AcceptVisitor(_searchQueryInterpreter, default);

            // Apply predicate to filter metadata
            filteredMetadata = predicate(allMetadata);

            int filteredCount = filteredMetadata.Count();
            LogSearchFilterResults(_logger, allMetadata.Count, filteredCount);
        }

        // Step 2.5: Apply surrogate ID range filtering for export partitioning
        // For file-based storage, we use index position as the "surrogate ID"
        if (options.StartSurrogateId.HasValue && options.EndSurrogateId.HasValue)
        {
            var filteredList = filteredMetadata.ToList();
            filteredMetadata = filteredList
                .Skip((int)options.StartSurrogateId.Value)
                .Take((int)(options.EndSurrogateId.Value - options.StartSurrogateId.Value + 1));

            LogSurrogateIdRangeFilter(_logger, options.StartSurrogateId.Value, options.EndSurrogateId.Value);
        }

        // Step 3: Apply pagination
        int skip = 0; // TODO: Parse continuation token
        int take = options.MaxItemCount;

        var pagedKeys = filteredMetadata
            .Skip(skip)
            .Take(take)
            .Select(m => m.Location)
            .ToList();

        LogPagination(_logger, skip, take, pagedKeys.Count);

        // Step 4: Stream ONLY the matching resources
        int streamed = 0;
        foreach (var key in pagedKeys)
        {
            ct.ThrowIfCancellationRequested();

            var resource = await _repository.GetAsync(key, ct);
            if (resource != null)
            {
                streamed++;
                yield return resource;
            }
        }

        int totalMatching = filteredMetadata.Count();
        LogStreamingSearchCompleted(_logger, streamed, totalMatching);
    }

    public async ValueTask<int> CountAsync<TSearchOptions>(
        TSearchOptions searchOptions,
        CancellationToken ct = default)
        where TSearchOptions : class
    {
        if (searchOptions is not SearchOptions options)
        {
            throw new ArgumentException($"Expected SearchOptions, got {typeof(TSearchOptions).Name}", nameof(searchOptions));
        }

        LogCounting(_logger, options.ResourceType, options.Expression != null);

        var resourceType = options.ResourceType;

        // Step 1: Load metadata with search indices (lightweight - no resource JSON loading)
        var allMetadata = await _repository.GetResourceMetadataAsync(resourceType, ct);

        if (allMetadata.Count == 0)
        {
            LogNoResourcesFound(_logger, resourceType);
            return 0;
        }

        LogLoadedMetadata(_logger, allMetadata.Count, resourceType);

        // Step 2: Apply search expression filter if provided
        int count;

        if (options.Expression != null)
        {
            LogApplyingSearchFilter(_logger);

            // Convert expression to predicate using SearchQueryInterpreter
            var predicate = options.Expression.AcceptVisitor(_searchQueryInterpreter, default);

            // Apply predicate to filter metadata
            var filteredMetadata = predicate(allMetadata);

            count = filteredMetadata.Count();

            LogSearchFilterResults(_logger, allMetadata.Count, count);
        }
        else
        {
            count = allMetadata.Count;
        }

        LogCountResult(_logger, resourceType, count);

        return count;
    }

    public async Task<IReadOnlyList<(long StartId, long EndId)>> GetExportRangesAsync(
        string resourceType,
        int numberOfRanges,
        CancellationToken ct = default)
    {
        LogGettingExportRanges(_logger, resourceType, numberOfRanges);

        // Load metadata for resource type
        var allMetadata = await _repository.GetResourceMetadataAsync(resourceType, ct);

        if (allMetadata.Count == 0)
        {
            LogNoResourcesForExport(_logger, resourceType);
            return Array.Empty<(long, long)>();
        }

        LogFoundResources(_logger, allMetadata.Count, resourceType);

        // For file-based storage, we use the index position as the "surrogate ID"
        // Partition based on count, not actual ID values
        long totalCount = allMetadata.Count;
        long rangeSize = (long)Math.Ceiling((double)totalCount / numberOfRanges);

        var ranges = new List<(long, long)>();
        long currentStart = 0;

        for (int i = 0; i < numberOfRanges; i++)
        {
            long currentEnd = (i == numberOfRanges - 1)
                ? totalCount - 1  // Last range includes all remaining items
                : currentStart + rangeSize - 1;

            // Only add range if there's data in it
            if (currentStart < totalCount)
            {
                long endId = Math.Min(currentEnd, totalCount - 1);
                ranges.Add((currentStart, endId));
                LogExportRange(_logger, i + 1, currentStart, endId);
                currentStart = currentEnd + 1;
            }
        }

        LogGeneratedExportRanges(_logger, ranges.Count, resourceType);

        return ranges.AsReadOnly();
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Searching for {ResourceType} resources (Expression: {HasExpression})")]
    private static partial void LogSearching(ILogger logger, string resourceType, bool hasExpression);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "No resources found for type {ResourceType}")]
    private static partial void LogNoResourcesFound(ILogger logger, string resourceType);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "Loaded metadata for {Count} {ResourceType} resources")]
    private static partial void LogLoadedMetadata(ILogger logger, int count, string resourceType);

    [LoggerMessage(EventId = 4, Level = LogLevel.Debug, Message = "Applying search expression filter")]
    private static partial void LogApplyingSearchFilter(ILogger logger);

    [LoggerMessage(EventId = 5, Level = LogLevel.Debug, Message = "Search expression filtered {Original} resources to {Filtered} results")]
    private static partial void LogSearchFilterResults(ILogger logger, int original, int filtered);

    [LoggerMessage(EventId = 6, Level = LogLevel.Debug, Message = "Applied surrogate ID range filter (index positions): [{StartId}..{EndId}]")]
    private static partial void LogSurrogateIdRangeFilter(ILogger logger, long startId, long endId);

    [LoggerMessage(EventId = 7, Level = LogLevel.Debug, Message = "Pagination: Skip={Skip}, Take={Take}, Results={ResultCount}")]
    private static partial void LogPagination(ILogger logger, int skip, int take, int resultCount);

    [LoggerMessage(EventId = 8, Level = LogLevel.Information, Message = "Search returned {Count} results (total matching: {Total}, page size: {PageSize})")]
    private static partial void LogSearchResults(ILogger logger, int count, int total, int pageSize);

    [LoggerMessage(EventId = 9, Level = LogLevel.Information, Message = "Streaming search for {ResourceType} resources (Expression: {HasExpression})")]
    private static partial void LogStreamingSearch(ILogger logger, string resourceType, bool hasExpression);

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "Streaming search completed: {Count} resources streamed (total matching: {Total})")]
    private static partial void LogStreamingSearchCompleted(ILogger logger, int count, int total);

    [LoggerMessage(EventId = 11, Level = LogLevel.Information, Message = "Counting {ResourceType} resources (Expression: {HasExpression})")]
    private static partial void LogCounting(ILogger logger, string resourceType, bool hasExpression);

    [LoggerMessage(EventId = 12, Level = LogLevel.Information, Message = "Count query for {ResourceType}: {Count} resources")]
    private static partial void LogCountResult(ILogger logger, string resourceType, int count);

    [LoggerMessage(EventId = 13, Level = LogLevel.Information, Message = "Getting export ranges: ResourceType={ResourceType}, NumberOfRanges={NumberOfRanges}")]
    private static partial void LogGettingExportRanges(ILogger logger, string resourceType, int numberOfRanges);

    [LoggerMessage(EventId = 14, Level = LogLevel.Information, Message = "No resources found for ResourceType={ResourceType}")]
    private static partial void LogNoResourcesForExport(ILogger logger, string resourceType);

    [LoggerMessage(EventId = 15, Level = LogLevel.Information, Message = "Found {Count} resources for ResourceType={ResourceType}")]
    private static partial void LogFoundResources(ILogger logger, int count, string resourceType);

    [LoggerMessage(EventId = 16, Level = LogLevel.Debug, Message = "Range {Index}: [{StartId}..{EndId}]")]
    private static partial void LogExportRange(ILogger logger, int index, long startId, long endId);

    [LoggerMessage(EventId = 17, Level = LogLevel.Information, Message = "Generated {RangeCount} export ranges for ResourceType={ResourceType}")]
    private static partial void LogGeneratedExportRanges(ILogger logger, int rangeCount, string resourceType);
}
