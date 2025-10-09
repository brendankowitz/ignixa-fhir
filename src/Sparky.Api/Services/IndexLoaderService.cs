// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Text.Json;
using Sparky.DataLayer.FileSystem.FileSystem;
using Sparky.DataLayer.InMemoryIndex;
using Sparky.Domain.Models;

namespace Sparky.Api.Services;

/// <summary>
/// Background service that loads resource metadata on startup to populate the in-memory index.
/// Ensures that resources persisted to disk are available after server restart (F5 developer experience).
/// </summary>
public class IndexLoaderService : IHostedService
{
    private readonly FileBasedFhirRepository _repository;
    private readonly IResourceLocationIndex _index;
    private readonly ILogger<IndexLoaderService> _logger;

    public IndexLoaderService(
        FileBasedFhirRepository repository,
        IResourceLocationIndex index,
        ILogger<IndexLoaderService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _index = index ?? throw new ArgumentNullException(nameof(index));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Scans all metadata files and populates the resource location index.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("IndexLoaderService starting - scanning metadata files...");

        var stopwatch = Stopwatch.StartNew();
        int resourceCount = 0;
        int errorCount = 0;

        try
        {
            // Get all metadata files from the repository
            var metadataFiles = _repository.GetAllMetadataFiles();

            foreach (var metadataFile in metadataFiles)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    // Read and parse metadata
                    string metadataJson = await File.ReadAllTextAsync(metadataFile, cancellationToken).ConfigureAwait(false);
                    var metadata = JsonSerializer.Deserialize<ResourceMetadataDto>(metadataJson);

                    if (metadata != null && !string.IsNullOrEmpty(metadata.ResourceType) && !string.IsNullOrEmpty(metadata.ResourceId))
                    {
                        // Add to index
                        var key = new ResourceKey(metadata.ResourceType, metadata.ResourceId, metadata.VersionId);
                        await _index.AddAsync(key, FileBasedFhirRepository.DataLayerName, cancellationToken).ConfigureAwait(false);

                        resourceCount++;

                        if (resourceCount % 100 == 0)
                        {
                            _logger.LogDebug("Loaded {Count} resources...", resourceCount);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load metadata from {File}", metadataFile);
                    errorCount++;
                }
            }

            stopwatch.Stop();

            _logger.LogInformation(
                "IndexLoaderService completed: Loaded {ResourceCount} resources in {ElapsedMs:N0}ms ({ErrorCount} errors)",
                resourceCount,
                stopwatch.ElapsedMilliseconds,
                errorCount);

            // Log performance warning if slow (target: <3s for 1,000 resources)
            if (resourceCount > 0)
            {
                double msPerResource = (double)stopwatch.ElapsedMilliseconds / resourceCount;
                if (msPerResource > 3.0)
                {
                    _logger.LogWarning(
                        "IndexLoaderService performance is slow: {MsPerResource:N2}ms per resource (target: <3ms)",
                        msPerResource);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IndexLoaderService failed during startup");
            throw;
        }
    }

    /// <summary>
    /// No-op for shutdown.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("IndexLoaderService stopping");
        return Task.CompletedTask;
    }

    /// <summary>
    /// DTO for deserializing metadata files.
    /// </summary>
    private class ResourceMetadataDto
    {
        public string ResourceType { get; set; } = string.Empty;
        public string ResourceId { get; set; } = string.Empty;
        public string VersionId { get; set; } = "1";
        public DateTimeOffset LastModified { get; set; }
    }
}
