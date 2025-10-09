// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging;
using Sparky.Domain.Abstractions;
using Sparky.Domain.Models;

namespace Sparky.DataLayer.FileSystem.FileSystem;

/// <summary>
/// File-based FHIR repository implementation for prototype.
/// Stores resources as JSON files with metadata in sidecar .meta.json files.
/// </summary>
/// <remarks>
/// Directory structure: {baseDir}/{resourceType}/{id}.json
/// Metadata: {baseDir}/{resourceType}/{id}.meta.json
/// </remarks>
public sealed class FileBasedFhirRepository : IFhirRepository, IDisposable
{
    private readonly string _baseDirectory;
    private readonly ILogger<FileBasedFhirRepository> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public FileBasedFhirRepository(string baseDirectory, ILogger<FileBasedFhirRepository> logger)
    {
        _baseDirectory = baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Directory.CreateDirectory(_baseDirectory);
    }

    public void Dispose()
    {
        _writeLock?.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask<ResourceWrapper?> GetAsync(ResourceKey key, CancellationToken ct = default)
    {
        string resourcePath = GetResourcePath(key);
        string metadataPath = GetMetadataPath(key);

        if (!File.Exists(resourcePath))
        {
            _logger.LogDebug("Resource not found: {ResourceType}/{Id}", key.ResourceType, key.Id);
            return null;
        }

        try
        {
            // Read resource JSON
            string resourceJson = await File.ReadAllTextAsync(resourcePath, ct);
            ISourceNode sourceNode = await FhirJsonNode.ParseAsync(resourceJson);

            // Read metadata
            ResourceMetadata metadata;
            if (File.Exists(metadataPath))
            {
                string metadataJson = await File.ReadAllTextAsync(metadataPath, ct);
                metadata = JsonSerializer.Deserialize<ResourceMetadata>(metadataJson)
                    ?? throw new InvalidOperationException($"Failed to deserialize metadata for {key}");
            }
            else
            {
                // Create default metadata if missing
                metadata = new ResourceMetadata
                {
                    VersionId = "1",
                    LastModified = File.GetLastWriteTimeUtc(resourcePath),
                    IsDeleted = false
                };
            }

            var wrapper = new ResourceWrapper(
                key.ResourceType,
                key.Id,
                metadata.VersionId,
                metadata.LastModified,
                sourceNode,
                metadata.Request,
                metadata.IsDeleted)
            {
                RawJson = resourceJson
            };

            _logger.LogDebug("Retrieved resource: {ResourceType}/{Id} version {VersionId}",
                key.ResourceType, key.Id, metadata.VersionId);

            return wrapper;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read resource: {ResourceType}/{Id}", key.ResourceType, key.Id);
            throw;
        }
    }

    public async ValueTask<ResourceKey> CreateOrUpdateAsync(ResourceWrapper resource, CancellationToken ct = default)
    {
        var key = new ResourceKey(resource.ResourceType, resource.ResourceId);
        string resourcePath = GetResourcePath(key);
        string metadataPath = GetMetadataPath(key);

        await _writeLock.WaitAsync(ct);
        try
        {
            // Ensure directory exists
            string directory = Path.GetDirectoryName(resourcePath)!;
            Directory.CreateDirectory(directory);

            // Increment version
            int newVersion = await GetNextVersionAsync(key, ct);

            // Serialize resource
            string resourceJson = resource.Resource.ToJson();

            // Write resource
            await File.WriteAllTextAsync(resourcePath, resourceJson, ct);

            // Write metadata
            var metadata = new ResourceMetadata
            {
                VersionId = newVersion.ToString(),
                LastModified = DateTimeOffset.UtcNow,
                IsDeleted = resource.IsDeleted,
                Request = resource.Request
            };

            string metadataJson = JsonSerializer.Serialize(metadata, _jsonOptions);
            await File.WriteAllTextAsync(metadataPath, metadataJson, ct);

            var resultKey = new ResourceKey(resource.ResourceType, resource.ResourceId, metadata.VersionId);

            _logger.LogInformation("Stored resource: {ResourceType}/{Id} version {VersionId}",
                resource.ResourceType, resource.ResourceId, metadata.VersionId);

            return resultKey;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async ValueTask<int> GetNextVersionAsync(ResourceKey key, CancellationToken ct)
    {
        string metadataPath = GetMetadataPath(key);

        if (!File.Exists(metadataPath))
        {
            return 1;
        }

        try
        {
            string metadataJson = await File.ReadAllTextAsync(metadataPath, ct);
            var metadata = JsonSerializer.Deserialize<ResourceMetadata>(metadataJson);
            return int.Parse(metadata?.VersionId ?? "0") + 1;
        }
        catch
        {
            return 1;
        }
    }

    private string GetResourcePath(ResourceKey key)
    {
        return Path.Combine(_baseDirectory, key.ResourceType, $"{key.Id}.json");
    }

    private string GetMetadataPath(ResourceKey key)
    {
        return Path.Combine(_baseDirectory, key.ResourceType, $"{key.Id}.meta.json");
    }

    private class ResourceMetadata
    {
        public string VersionId { get; set; } = "1";
        public DateTimeOffset LastModified { get; set; }
        public bool IsDeleted { get; set; }
        public ResourceRequest Request { get; set; } = new ResourceRequest("PUT", "");
    }
}
