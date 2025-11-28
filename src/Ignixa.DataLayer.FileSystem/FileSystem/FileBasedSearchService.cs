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
public class FileBasedSearchService : ISearchService
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

        _logger.LogInformation(
            "Searching for {ResourceType} resources (Expression: {HasExpression})",
            options.ResourceType,
            options.Expression != null);

        var resourceType = options.ResourceType;

        // Step 1: Load metadata with search indices (lightweight - no resource JSON loading)
        var allMetadata = await _repository.GetResourceMetadataAsync(resourceType, ct);

        if (allMetadata.Count == 0)
        {
            _logger.LogDebug("No resources found for type {ResourceType}", resourceType);
            return Array.Empty<SearchEntryResult>();
        }

        _logger.LogDebug(
            "Loaded metadata for {Count} {ResourceType} resources",
            allMetadata.Count,
            resourceType);

        // Step 2: Apply search expression filter if provided
        IEnumerable<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> filteredMetadata = allMetadata;

        if (options.Expression != null)
        {
            _logger.LogDebug("Applying search expression filter");

            // Convert expression to predicate using SearchQueryInterpreter
            var predicate = options.Expression.AcceptVisitor(_searchQueryInterpreter, default);

            // Apply predicate to filter metadata
            filteredMetadata = predicate(allMetadata);

            _logger.LogDebug(
                "Search expression filtered {Original} resources to {Filtered} results",
                allMetadata.Count,
                filteredMetadata.Count());
        }

        // Step 2.5: Apply surrogate ID range filtering for export partitioning
        // For file-based storage, we use index position as the "surrogate ID"
        if (options.StartSurrogateId.HasValue && options.EndSurrogateId.HasValue)
        {
            var filteredList = filteredMetadata.ToList();
            filteredMetadata = filteredList
                .Skip((int)options.StartSurrogateId.Value)
                .Take((int)(options.EndSurrogateId.Value - options.StartSurrogateId.Value + 1));

            _logger.LogDebug(
                "Applied surrogate ID range filter (index positions): [{StartId}..{EndId}]",
                options.StartSurrogateId.Value,
                options.EndSurrogateId.Value);
        }

        // Step 3: Apply pagination
        int skip = 0; // TODO: Parse continuation token
        int take = options.MaxItemCount;

        var pagedKeys = filteredMetadata
            .Skip(skip)
            .Take(take)
            .Select(m => m.Location)
            .ToList();

        _logger.LogDebug(
            "Pagination: Skip={Skip}, Take={Take}, Results={ResultCount}",
            skip,
            take,
            pagedKeys.Count);

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

        _logger.LogInformation(
            "Search returned {Count} results (total matching: {Total}, page size: {PageSize})",
            results.Count,
            filteredMetadata.Count(),
            take);

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

        _logger.LogInformation(
            "Streaming search for {ResourceType} resources (Expression: {HasExpression})",
            options.ResourceType,
            options.Expression != null);

        var resourceType = options.ResourceType;

        // Step 1: Load metadata with search indices (lightweight - no resource JSON loading)
        var allMetadata = await _repository.GetResourceMetadataAsync(resourceType, ct);

        if (allMetadata.Count == 0)
        {
            _logger.LogDebug("No resources found for type {ResourceType}", resourceType);
            yield break;
        }

        _logger.LogDebug(
            "Loaded metadata for {Count} {ResourceType} resources",
            allMetadata.Count,
            resourceType);

        // Step 2: Apply search expression filter if provided
        IEnumerable<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> filteredMetadata = allMetadata;

        if (options.Expression != null)
        {
            _logger.LogDebug("Applying search expression filter");

            // Convert expression to predicate using SearchQueryInterpreter
            var predicate = options.Expression.AcceptVisitor(_searchQueryInterpreter, default);

            // Apply predicate to filter metadata
            filteredMetadata = predicate(allMetadata);

            _logger.LogDebug(
                "Search expression filtered {Original} resources to {Filtered} results",
                allMetadata.Count,
                filteredMetadata.Count());
        }

        // Step 2.5: Apply surrogate ID range filtering for export partitioning
        // For file-based storage, we use index position as the "surrogate ID"
        if (options.StartSurrogateId.HasValue && options.EndSurrogateId.HasValue)
        {
            var filteredList = filteredMetadata.ToList();
            filteredMetadata = filteredList
                .Skip((int)options.StartSurrogateId.Value)
                .Take((int)(options.EndSurrogateId.Value - options.StartSurrogateId.Value + 1));

            _logger.LogDebug(
                "Applied surrogate ID range filter (index positions): [{StartId}..{EndId}]",
                options.StartSurrogateId.Value,
                options.EndSurrogateId.Value);
        }

        // Step 3: Apply pagination
        int skip = 0; // TODO: Parse continuation token
        int take = options.MaxItemCount;

        var pagedKeys = filteredMetadata
            .Skip(skip)
            .Take(take)
            .Select(m => m.Location)
            .ToList();

        _logger.LogDebug(
            "Pagination: Skip={Skip}, Take={Take}, Results={ResultCount}",
            skip,
            take,
            pagedKeys.Count);

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

        _logger.LogInformation(
            "Streaming search completed: {Count} resources streamed (total matching: {Total})",
            streamed,
            filteredMetadata.Count());
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

        _logger.LogInformation(
            "Counting {ResourceType} resources (Expression: {HasExpression})",
            options.ResourceType,
            options.Expression != null);

        var resourceType = options.ResourceType;

        // Step 1: Load metadata with search indices (lightweight - no resource JSON loading)
        var allMetadata = await _repository.GetResourceMetadataAsync(resourceType, ct);

        if (allMetadata.Count == 0)
        {
            _logger.LogDebug("No resources found for type {ResourceType}", resourceType);
            return 0;
        }

        _logger.LogDebug(
            "Loaded metadata for {Count} {ResourceType} resources",
            allMetadata.Count,
            resourceType);

        // Step 2: Apply search expression filter if provided
        int count;

        if (options.Expression != null)
        {
            _logger.LogDebug("Applying search expression filter");

            // Convert expression to predicate using SearchQueryInterpreter
            var predicate = options.Expression.AcceptVisitor(_searchQueryInterpreter, default);

            // Apply predicate to filter metadata
            var filteredMetadata = predicate(allMetadata);

            count = filteredMetadata.Count();

            _logger.LogDebug(
                "Search expression filtered {Original} resources to {Filtered} results",
                allMetadata.Count,
                count);
        }
        else
        {
            count = allMetadata.Count;
        }

        _logger.LogInformation(
            "Count query for {ResourceType}: {Count} resources",
            resourceType,
            count);

        return count;
    }

    public async Task<IReadOnlyList<(long StartId, long EndId)>> GetExportRangesAsync(
        string resourceType,
        int numberOfRanges,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Getting export ranges: ResourceType={ResourceType}, NumberOfRanges={NumberOfRanges}",
            resourceType,
            numberOfRanges);

        // Load metadata for resource type
        var allMetadata = await _repository.GetResourceMetadataAsync(resourceType, ct);

        if (allMetadata.Count == 0)
        {
            _logger.LogInformation("No resources found for ResourceType={ResourceType}", resourceType);
            return Array.Empty<(long, long)>();
        }

        _logger.LogInformation(
            "Found {Count} resources for ResourceType={ResourceType}",
            allMetadata.Count,
            resourceType);

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
                ranges.Add((currentStart, Math.Min(currentEnd, totalCount - 1)));
                _logger.LogDebug("Range {Index}: [{StartId}..{EndId}]", i + 1, currentStart, Math.Min(currentEnd, totalCount - 1));
                currentStart = currentEnd + 1;
            }
        }

        _logger.LogInformation(
            "Generated {RangeCount} export ranges for ResourceType={ResourceType}",
            ranges.Count,
            resourceType);

        return ranges.AsReadOnly();
    }
}
