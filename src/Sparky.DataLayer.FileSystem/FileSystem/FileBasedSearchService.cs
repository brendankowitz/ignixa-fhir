// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Sparky.Domain.Abstractions;
using Sparky.Domain.Models;
using Sparky.Search.Models;

namespace Sparky.DataLayer.FileSystem.FileSystem;

/// <summary>
/// File-based implementation of search service.
/// Phase 1.2: Simple in-memory filtering (loads all resources, filters in memory).
/// </summary>
public class FileBasedSearchService : ISearchService
{
    private readonly IFhirRepository _repository;
    private readonly ILogger<FileBasedSearchService> _logger;
    private readonly string _baseDirectory;

    public FileBasedSearchService(
        IFhirRepository repository,
        ILogger<FileBasedSearchService> logger,
        string baseDirectory)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _baseDirectory = baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory));
    }

    public async ValueTask<IReadOnlyList<ResourceWrapper>> SearchAsync<TSearchOptions>(
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

        // Phase 1.2: Simple implementation - load all resources of the type, filter in memory
        // TODO Phase 1.2a: Add search indexing and optimized querying

        var resourceType = options.ResourceType;
        var resourceDir = Path.Combine(_baseDirectory, resourceType);

        if (!Directory.Exists(resourceDir))
        {
            _logger.LogDebug("Resource directory not found: {ResourceDir}", resourceDir);
            return Array.Empty<ResourceWrapper>();
        }

        // Load all resource IDs
        var resourceFiles = Directory.GetFiles(resourceDir, "*.json")
            .Where(f => !f.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        _logger.LogDebug("Found {Count} {ResourceType} resources on disk", resourceFiles.Count, resourceType);

        var results = new List<ResourceWrapper>();

        // Load each resource
        foreach (var filePath in resourceFiles)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var resourceKey = new ResourceKey(resourceType, fileName);

            var resource = await _repository.GetAsync(resourceKey, ct);
            if (resource != null)
            {
                // TODO: Apply expression filtering when search indexing is implemented
                // For now, return all resources (Phase 1.2 prototype behavior)
                results.Add(resource);
            }
        }

        // Apply pagination
        int skip = 0; // TODO: Parse continuation token
        int take = options.MaxItemCount;

        var pagedResults = results.Skip(skip).Take(take).ToList();

        _logger.LogInformation(
            "Search returned {Count} results (total: {Total}, page size: {PageSize})",
            pagedResults.Count,
            results.Count,
            take);

        return pagedResults;
    }

    public async IAsyncEnumerable<ResourceWrapper> SearchStreamAsync<TSearchOptions>(
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
        var resourceDir = Path.Combine(_baseDirectory, resourceType);

        if (!Directory.Exists(resourceDir))
        {
            _logger.LogDebug("Resource directory not found: {ResourceDir}", resourceDir);
            yield break;
        }

        // Load all resource IDs
        var resourceFiles = Directory.GetFiles(resourceDir, "*.json")
            .Where(f => !f.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        _logger.LogDebug("Found {Count} {ResourceType} resources on disk", resourceFiles.Count, resourceType);

        // Apply pagination parameters
        int skip = 0; // TODO: Parse continuation token
        int take = options.MaxItemCount;

        int streamed = 0;
        int skipped = 0;

        // Stream each resource as it's loaded
        foreach (var filePath in resourceFiles)
        {
            ct.ThrowIfCancellationRequested();

            // Skip resources before the page start
            if (skipped < skip)
            {
                skipped++;
                continue;
            }

            // Stop after reaching page size limit
            if (streamed >= take)
            {
                break;
            }

            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var resourceKey = new ResourceKey(resourceType, fileName);

            var resource = await _repository.GetAsync(resourceKey, ct);
            if (resource != null)
            {
                // TODO: Apply expression filtering when search indexing is implemented
                // For now, return all resources (Phase 1.2 prototype behavior)
                streamed++;
                yield return resource;
            }
        }

        _logger.LogInformation(
            "Streaming search completed: {Count} resources streamed",
            streamed);
    }

    public ValueTask<int> CountAsync<TSearchOptions>(
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

        // Phase 1.2: Simple implementation - count files on disk
        // Ignores _sort, _include, _revinclude (as per spec - count only considers filters)
        // TODO Phase 1.2a: Use search index for optimized counting

        var resourceType = options.ResourceType;
        var resourceDir = Path.Combine(_baseDirectory, resourceType);

        if (!Directory.Exists(resourceDir))
        {
            _logger.LogDebug("Resource directory not found: {ResourceDir}", resourceDir);
            return ValueTask.FromResult(0);
        }

        // Count resource files (exclude .meta.json files)
        var count = Directory.GetFiles(resourceDir, "*.json")
            .Count(f => !f.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase));

        _logger.LogInformation(
            "Count query for {ResourceType}: {Count} resources",
            resourceType,
            count);

        // TODO: Apply expression filtering when search indexing is implemented
        // For now, return total count (Phase 1.2 prototype behavior)
        return ValueTask.FromResult(count);
    }
}
