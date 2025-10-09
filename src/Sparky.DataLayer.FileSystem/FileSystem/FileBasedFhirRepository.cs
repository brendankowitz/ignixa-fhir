// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using Hl7.Fhir.ElementModel;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using Sparky.Domain.Abstractions;
using Sparky.Domain.Models;
using Sparky.SourceNodeSerialization;
using Sparky.SourceNodeSerialization.SourceNodes.Models;

namespace Sparky.DataLayer.FileSystem.FileSystem;

/// <summary>
/// File-based FHIR repository implementation for prototype.
/// Stores resources as NDJSON files with metadata in sidecar .metadata.ndjson files.
/// </summary>
/// <remarks>
/// Directory structure: {baseDir}/{resourceType}/{YYYY}/{MM}/{DD}/tx-{transactionId}.ndjson
/// Metadata: {baseDir}/{resourceType}/{YYYY}/{MM}/{DD}/tx-{transactionId}.metadata.ndjson
/// </remarks>
public sealed class FileBasedFhirRepository : IFhirRepository, IDisposable
{
    private readonly string _baseDirectory;
    private readonly ILogger<FileBasedFhirRepository> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };
    private readonly RecyclableMemoryStreamManager _memoryStreamManager;

    /// <summary>
    /// Data layer name for resource location index.
    /// </summary>
    public const string DataLayerName = "FileSystem";

    public FileBasedFhirRepository(
        string baseDirectory,
        ILogger<FileBasedFhirRepository> logger,
        RecyclableMemoryStreamManager? memoryStreamManager = null)
    {
        _baseDirectory = baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _memoryStreamManager = memoryStreamManager ?? new RecyclableMemoryStreamManager();

        Directory.CreateDirectory(_baseDirectory);
    }

    public void Dispose()
    {
        _writeLock?.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask<ResourceWrapper?> GetAsync(ResourceKey key, CancellationToken ct = default)
    {
        try
        {
            // Find the metadata file for this resource
            var metadataFile = await FindLatestMetadataFileAsync(key, ct).ConfigureAwait(false);
            if (metadataFile == null)
            {
                _logger.LogDebug("Resource not found: {ResourceType}/{Id}", key.ResourceType, key.Id);
                return null;
            }

            // Read metadata
            var metadata = await ReadMetadataFileAsync(metadataFile, ct).ConfigureAwait(false);

            // Read resource from NDJSON file (line 2)
            string ndjsonPath = metadataFile.Replace(".metadata.ndjson", ".ndjson", StringComparison.Ordinal);
            string resourceJson = await ReadResourceFromNdjsonAsync(ndjsonPath, ct).ConfigureAwait(false);

            // Convert to UTF-8 bytes for zero-copy serialization
            byte[] resourceJsonBytes = Encoding.UTF8.GetBytes(resourceJson);

            // Parse using JsonSourceNodeFactory
            ISourceNode sourceNode = JsonSourceNodeFactory.Parse(resourceJson, key.ResourceType);

            var wrapper = new ResourceWrapper(
                key.ResourceType,
                key.Id,
                metadata.VersionId,
                metadata.LastModified,
                sourceNode,
                metadata.Request,
                metadata.IsDeleted)
            {
                RawJson = resourceJson,
                RawJsonBytes = new ReadOnlyMemory<byte>(resourceJsonBytes)
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

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Generate transaction ID
            var transactionId = TransactionId.Generate();
            var timestamp = DateTimeOffset.UtcNow;

            // Increment version
            int newVersion = await GetNextVersionAsync(key, ct).ConfigureAwait(false);

            // Use RawJson if available (fast path), otherwise would need complex serialization
            string resourceJson = resource.RawJson
                ?? throw new InvalidOperationException("RawJson must be provided for FileBasedFhirRepository");

            // Get date-based directory path
            string dateDirectory = GetDateDirectory(resource.ResourceType, timestamp);
            Directory.CreateDirectory(dateDirectory);

            // Generate file paths
            string ndjsonPath = Path.Combine(dateDirectory, $"tx-{transactionId}.ndjson");
            string metadataPath = Path.Combine(dateDirectory, $"tx-{transactionId}.metadata.ndjson");

            // Create transaction bundle (line 1 of NDJSON)
            var bundle = new
            {
                resourceType = "Bundle",
                type = "transaction",
                id = transactionId.ToString(),
                timestamp = timestamp.ToString("o"),
                entry = new[]
                {
                    new
                    {
                        request = new
                        {
                            method = resource.Request.Method,
                            url = resource.Request.Url
                        }
                    }
                }
            };

            // Write NDJSON file (line 1: bundle, line 2: resource)
            using (var stream = _memoryStreamManager.GetStream("ndjson-write"))
            using (var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(bundle, _jsonOptions)).ConfigureAwait(false);
                await writer.WriteLineAsync(resourceJson).ConfigureAwait(false);
                await writer.FlushAsync(ct).ConfigureAwait(false);

                stream.Position = 0;
                using var fileStream = new FileStream(ndjsonPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
                await stream.CopyToAsync(fileStream, ct).ConfigureAwait(false);
            }

            // Write metadata sidecar
            var metadata = new ResourceMetadata
            {
                TransactionId = transactionId.ToString(),
                ResourceType = resource.ResourceType,
                ResourceId = resource.ResourceId,
                VersionId = newVersion.ToString(),
                LastModified = timestamp,
                IsDeleted = resource.IsDeleted,
                Request = resource.Request,
                SearchIndexes = new List<SearchIndexMetadata>() // TODO: Extract search indexes
            };

            string metadataJson = JsonSerializer.Serialize(metadata, _jsonOptions);
            await File.WriteAllTextAsync(metadataPath, metadataJson, ct).ConfigureAwait(false);

            var resultKey = new ResourceKey(resource.ResourceType, resource.ResourceId, metadata.VersionId);

            _logger.LogInformation("Stored resource: {ResourceType}/{Id} version {VersionId} tx {TransactionId}",
                resource.ResourceType, resource.ResourceId, metadata.VersionId, transactionId);

            return resultKey;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async ValueTask<int> GetNextVersionAsync(ResourceKey key, CancellationToken ct)
    {
        var metadataFile = await FindLatestMetadataFileAsync(key, ct).ConfigureAwait(false);
        if (metadataFile == null)
        {
            return 1;
        }

        try
        {
            var metadata = await ReadMetadataFileAsync(metadataFile, ct).ConfigureAwait(false);
            return int.Parse(metadata.VersionId) + 1;
        }
        catch
        {
            return 1;
        }
    }

    private string GetDateDirectory(string resourceType, DateTimeOffset timestamp)
    {
        return Path.Combine(
            _baseDirectory,
            resourceType,
            timestamp.Year.ToString("D4"),
            timestamp.Month.ToString("D2"),
            timestamp.Day.ToString("D2"));
    }

    private async ValueTask<string?> FindLatestMetadataFileAsync(ResourceKey key, CancellationToken ct)
    {
        string resourceTypeDir = Path.Combine(_baseDirectory, key.ResourceType);
        if (!Directory.Exists(resourceTypeDir))
        {
            return null;
        }

        // Search all date-based subdirectories for metadata files matching this resource
        var metadataFiles = Directory.GetFiles(resourceTypeDir, "*.metadata.ndjson", SearchOption.AllDirectories);

        string? latestFile = null;
        DateTimeOffset latestTimestamp = DateTimeOffset.MinValue;

        foreach (var file in metadataFiles)
        {
            try
            {
                var metadata = await ReadMetadataFileAsync(file, ct).ConfigureAwait(false);
                if (metadata.ResourceId == key.Id && metadata.LastModified > latestTimestamp)
                {
                    latestTimestamp = metadata.LastModified;
                    latestFile = file;
                }
            }
            catch
            {
                // Skip corrupted metadata files
            }
        }

        return latestFile;
    }

    private async ValueTask<ResourceMetadata> ReadMetadataFileAsync(string path, CancellationToken ct)
    {
        string metadataJson = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<ResourceMetadata>(metadataJson)
            ?? throw new InvalidOperationException($"Failed to deserialize metadata from {path}");
    }

    private async ValueTask<string> ReadResourceFromNdjsonAsync(string path, CancellationToken ct)
    {
        using var stream = _memoryStreamManager.GetStream("ndjson-read");
        using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        await fileStream.CopyToAsync(stream, ct).ConfigureAwait(false);

        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8);

        // Skip line 1 (bundle)
        await reader.ReadLineAsync(ct).ConfigureAwait(false);

        // Read line 2 (resource)
        string? resourceJson = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        return resourceJson ?? throw new InvalidOperationException($"NDJSON file {path} does not contain resource data on line 2");
    }

    /// <summary>
    /// Gets all metadata file paths for a given resource type.
    /// Used by IndexLoaderService to scan on startup.
    /// </summary>
    public IEnumerable<string> GetAllMetadataFiles(string? resourceType = null)
    {
        string searchDir = resourceType != null
            ? Path.Combine(_baseDirectory, resourceType)
            : _baseDirectory;

        if (!Directory.Exists(searchDir))
        {
            return Enumerable.Empty<string>();
        }

        return Directory.GetFiles(searchDir, "*.metadata.ndjson", SearchOption.AllDirectories);
    }

    private class ResourceMetadata
    {
        public string TransactionId { get; set; } = string.Empty;
        public string ResourceType { get; set; } = string.Empty;
        public string ResourceId { get; set; } = string.Empty;
        public string VersionId { get; set; } = "1";
        public DateTimeOffset LastModified { get; set; }
        public bool IsDeleted { get; set; }
        public ResourceRequest Request { get; set; } = new ResourceRequest("PUT", "");
        public List<SearchIndexMetadata> SearchIndexes { get; set; } = new List<SearchIndexMetadata>();
    }

    private class SearchIndexMetadata
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
