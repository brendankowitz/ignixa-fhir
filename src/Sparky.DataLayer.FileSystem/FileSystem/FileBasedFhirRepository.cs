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
using Sparky.Search.Indexing;
using Sparky.Search.Serialization;
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
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new CompactSearchIndexConverter() }
    };
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
            // Find the latest metadata file for this resource
            var metadataFile = await FindLatestMetadataFileAsync(key, ct).ConfigureAwait(false);
            if (metadataFile == null)
            {
                _logger.LogDebug("Resource not found: {ResourceType}/{Id}", key.ResourceType, key.Id);
                return null;
            }

            // Read metadata
            var metadata = await ReadMetadataFileAsync(metadataFile, ct).ConfigureAwait(false);

            // Extract transaction ID from metadata to locate resource file
            // Resource files are at: ResourceType/YYYY/MM/DD/tx-{transactionId}.ndjson
            var transactionTimestamp = metadata.LastModified;
            string resourceTypeDir = GetDateDirectory(key.ResourceType, transactionTimestamp);
            string ndjsonPath = Path.Combine(resourceTypeDir, $"tx-{metadata.TransactionId}.ndjson");

            // Read resource from NDJSON file
            string resourceJson = await ReadResourceFromNdjsonByIdAsync(ndjsonPath, key.Id, ct).ConfigureAwait(false);

            // Convert to UTF-8 bytes for zero-copy serialization
            byte[] resourceJsonBytes = Encoding.UTF8.GetBytes(resourceJson);

            // Parse to ResourceJsonNode to enable caching
            // Using cached ToSourceNode() prevents repeated ReflectedSourceNode allocations
            var resourceNode = ResourceJsonNode.Parse(resourceJson);
            ISourceNode sourceNode = resourceNode.ToSourceNode();

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

            // Write NDJSON file (just the resource JSON, no bundle header)
            // Transaction metadata is stored in /transactions lock file
            await File.WriteAllTextAsync(ndjsonPath, resourceJson, ct).ConfigureAwait(false);

            // Write metadata sidecar in _internal directory
            string internalMetadataDir = Path.Combine(
                _baseDirectory,
                "_internal",
                resource.ResourceType,
                resource.ResourceId);
            Directory.CreateDirectory(internalMetadataDir);

            string internalMetadataPath = Path.Combine(internalMetadataDir, $"{transactionId}.metadata.json");

            var metadata = new ResourceMetadata
            {
                TransactionId = transactionId.ToString(),
                ResourceType = resource.ResourceType,
                ResourceId = resource.ResourceId,
                VersionId = newVersion.ToString(),
                LastModified = timestamp,
                IsDeleted = resource.IsDeleted,
                Request = resource.Request,
                SearchIndexes = resource.SearchIndices?.Cast<SearchIndexEntry>().ToList(),
            };

            string metadataJson = JsonSerializer.Serialize(metadata, _jsonOptions);
            await File.WriteAllTextAsync(internalMetadataPath, metadataJson, ct).ConfigureAwait(false);

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
        // New sparse metadata location: _internal/ResourceType/[resourceid]/*.metadata.json
        string metadataDir = Path.Combine(_baseDirectory, "_internal", key.ResourceType, key.Id);
        if (!Directory.Exists(metadataDir))
        {
            return null;
        }

        // Find all metadata files for this resource
        var metadataFiles = Directory.GetFiles(metadataDir, "*.metadata.json", SearchOption.TopDirectoryOnly);

        string? latestFile = null;
        DateTimeOffset latestTimestamp = DateTimeOffset.MinValue;

        foreach (var file in metadataFiles)
        {
            try
            {
                var metadata = await ReadMetadataFileAsync(file, ct).ConfigureAwait(false);
                if (metadata.LastModified > latestTimestamp)
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
        return JsonSerializer.Deserialize<ResourceMetadata>(metadataJson, _jsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize metadata from {path}");
    }

    private async ValueTask<string> ReadResourceFromNdjsonByIdAsync(string path, string resourceId, CancellationToken ct)
    {
        // NDJSON file format: Just resources, one per line (no bundle header)
        // Transaction metadata is in /transactions files

        using var stream = _memoryStreamManager.GetStream("ndjson-read");
        using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        await fileStream.CopyToAsync(stream, ct).ConfigureAwait(false);

        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8);

        // Read all resource lines and find matching ID
        while (!reader.EndOfStream)
        {
            string? resourceJson = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(resourceJson))
            {
                continue;
            }

            // Parse to check if this is the resource we're looking for
            var jsonDoc = JsonDocument.Parse(resourceJson);
            var id = jsonDoc.RootElement.GetProperty("id").GetString();

            if (id == resourceId)
            {
                return resourceJson;
            }
        }

        throw new InvalidOperationException(
            $"Resource {resourceId} not found in NDJSON file {path}");
    }

    public async ValueTask<TransactionId> GetNextTransactionIdAsync(CancellationToken ct = default)
    {
        // Generate a new transaction ID
        // In a production system, this might allocate from a sequence or global counter
        return await ValueTask.FromResult(TransactionId.Generate());
    }

    public async ValueTask CommitTransactionAsync(TransactionId transactionId, CancellationToken ct = default)
    {
        var timestamp = DateTimeOffset.UtcNow;

        string transactionDir = Path.Combine(
            _baseDirectory,
            "_transactions",
            timestamp.Year.ToString("D4"),
            timestamp.Month.ToString("D2"),
            timestamp.Day.ToString("D2"));

        string lockFilePath = Path.Combine(transactionDir, $"tx-{transactionId}.lock.ndjson");
        string committedFilePath = Path.Combine(transactionDir, $"tx-{transactionId}.ndjson");

        if (!File.Exists(lockFilePath))
        {
            _logger.LogWarning("Lock file not found for transaction {TransactionId}: {LockFile}", transactionId, lockFilePath);
            return;
        }

        // Rename lock file to committed file
        File.Move(lockFilePath, committedFilePath);

        _logger.LogInformation("Transaction {TransactionId} committed: {CommittedFile}", transactionId, committedFilePath);

        await ValueTask.CompletedTask;
    }

    public async Task<IReadOnlyList<ResourceKey>> BatchWriteAsync(
        TransactionId transactionId,
        IReadOnlyList<(string resourceType, string resourceId, ISourceNode resource, string rawJson, IReadOnlyList<object> searchIndexes)> operations,
        CancellationToken ct = default)
    {
        if (operations == null || operations.Count == 0)
        {
            return Array.Empty<ResourceKey>();
        }

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var timestamp = DateTimeOffset.UtcNow;

            _logger.LogInformation(
                "Starting batch write of {Count} resources with transaction ID {TransactionId}",
                operations.Count,
                transactionId);

            // Step 1: Create lock file directory (_transactions/YYYY/MM/DD/)
            string transactionDir = Path.Combine(
                _baseDirectory,
                "_transactions",
                timestamp.Year.ToString("D4"),
                timestamp.Month.ToString("D2"),
                timestamp.Day.ToString("D2"));
            Directory.CreateDirectory(transactionDir);

            string lockFilePath = Path.Combine(transactionDir, $"tx-{transactionId}.lock.ndjson");
            string committedFilePath = Path.Combine(transactionDir, $"tx-{transactionId}.ndjson");

            // Step 2: Check for conflicts (only check committed file, lock file is expected for multi-batch transactions)
            if (File.Exists(committedFilePath))
            {
                throw new InvalidOperationException(
                    $"Transaction {transactionId} already committed: {committedFilePath}");
            }

            // Step 3: Group operations by resource type
            var operationsByType = operations
                .GroupBy(op => op.resourceType)
                .ToDictionary(g => g.Key, g => g.ToList());

            _logger.LogDebug(
                "Grouped {TotalCount} operations into {GroupCount} resource types",
                operations.Count,
                operationsByType.Count);

            // Step 4: Get next versions for all resources
            var results = new List<ResourceKey>();
            var resourceMetadata = new List<ResourceMetadata>();

            foreach (var operation in operations)
            {
                var key = new ResourceKey(operation.resourceType, operation.resourceId);
                int newVersion = await GetNextVersionAsync(key, ct).ConfigureAwait(false);

                var metadata = new ResourceMetadata
                {
                    TransactionId = transactionId.ToString(),
                    ResourceType = operation.resourceType,
                    ResourceId = operation.resourceId,
                    VersionId = newVersion.ToString(),
                    LastModified = timestamp,
                    IsDeleted = false,
                    Request = new ResourceRequest("PUT", $"{operation.resourceType}/{operation.resourceId}"),
                    SearchIndexes = operation.searchIndexes?.Cast<SearchIndexEntry>().ToList(),
                };

                resourceMetadata.Add(metadata);
                results.Add(new ResourceKey(operation.resourceType, operation.resourceId, metadata.VersionId));
            }

            // Step 5: Write or append to lock file with transaction log
            bool lockFileExists = File.Exists(lockFilePath);
            await WriteLockFileAsync(lockFilePath, transactionId, timestamp, operations, append: lockFileExists, ct).ConfigureAwait(false);

            if (lockFileExists)
            {
                _logger.LogDebug("Appended to lock file: {LockFile}", lockFilePath);
            }
            else
            {
                _logger.LogDebug("Created lock file: {LockFile}", lockFilePath);
            }

            // Step 6: Write resource files grouped by type (append mode)
            foreach (var group in operationsByType)
            {
                string resourceType = group.Key;
                var typeOperations = group.Value;

                // Get resource type directory path (ResourceType/YYYY/MM/DD/)
                string resourceTypeDir = GetDateDirectory(resourceType, timestamp);
                Directory.CreateDirectory(resourceTypeDir);

                string resourceFilePath = Path.Combine(resourceTypeDir, $"tx-{transactionId}.ndjson");
                bool fileExists = File.Exists(resourceFilePath);

                if (fileExists)
                {
                    _logger.LogDebug(
                        "Appending {Count} resources to existing file: {FilePath}",
                        typeOperations.Count,
                        resourceFilePath);
                }
                else
                {
                    _logger.LogDebug(
                        "Creating new file with {Count} resources: {FilePath}",
                        typeOperations.Count,
                        resourceFilePath);
                }

                // Write or append resources
                await WriteResourceFileAsync(
                    resourceFilePath,
                    transactionId,
                    timestamp,
                    typeOperations,
                    append: fileExists,
                    ct).ConfigureAwait(false);
            }

            // Step 7: Write sparse metadata sidecars (_internal/ResourceType/[resourceid]/[transactionid].metadata.json)
            foreach (var metadata in resourceMetadata)
            {
                string metadataDir = Path.Combine(
                    _baseDirectory,
                    "_internal",
                    metadata.ResourceType,
                    metadata.ResourceId);
                Directory.CreateDirectory(metadataDir);

                string metadataPath = Path.Combine(metadataDir, $"{metadata.TransactionId}.metadata.json");
                string metadataJson = JsonSerializer.Serialize(metadata, _jsonOptions);
                await File.WriteAllTextAsync(metadataPath, metadataJson, ct).ConfigureAwait(false);

                _logger.LogDebug(
                    "Metadata written: {MetadataPath}",
                    metadataPath);
            }

            // Step 8: Batch complete - lock file remains until transaction is committed externally
            _logger.LogInformation(
                "Batch write completed: {Count} resources written in transaction {TransactionId}",
                operations.Count,
                transactionId);

            return results;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async System.Threading.Tasks.Task WriteLockFileAsync(
        string path,
        TransactionId transactionId,
        DateTimeOffset timestamp,
        IReadOnlyList<(string resourceType, string resourceId, ISourceNode resource, string rawJson, IReadOnlyList<object> searchIndexes)> operations,
        bool append,
        CancellationToken ct)
    {
        if (append)
        {
            // Append batch operations to existing lock file (one line per operation)
            using var stream = _memoryStreamManager.GetStream("lock-file-append");
            using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);

            foreach (var op in operations)
            {
                var entry = new
                {
                    request = new
                    {
                        method = "PUT",
                        url = $"{op.resourceType}/{op.resourceId}"
                    }
                };
                await writer.WriteLineAsync(JsonSerializer.Serialize(entry, _jsonOptions)).ConfigureAwait(false);
            }

            await writer.FlushAsync(ct).ConfigureAwait(false);
            stream.Position = 0;

            using var fileStream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await stream.CopyToAsync(fileStream, ct).ConfigureAwait(false);
        }
        else
        {
            // Create new lock file with bundle header + first batch operations
            var manifest = new
            {
                resourceType = "Bundle",
                type = "transaction",
                id = transactionId.ToString(),
                timestamp = timestamp.ToString("o"),
                entry = operations.Select(op => new
                {
                    request = new
                    {
                        method = "PUT",
                        url = $"{op.resourceType}/{op.resourceId}"
                    }
                }).ToArray()
            };

            string manifestJson = JsonSerializer.Serialize(manifest, _jsonOptions);
            await File.WriteAllTextAsync(path, manifestJson, ct).ConfigureAwait(false);
        }
    }

    private async System.Threading.Tasks.Task WriteResourceFileAsync(
        string path,
        TransactionId transactionId,
        DateTimeOffset timestamp,
        List<(string resourceType, string resourceId, ISourceNode resource, string rawJson, IReadOnlyList<object> searchIndexes)> operations,
        bool append,
        CancellationToken ct)
    {
        using var stream = _memoryStreamManager.GetStream("resource-file-write");
        using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);

        // Write resource JSON (one per line, no bundle header)
        // Transaction metadata is stored in /transactions files
        foreach (var operation in operations)
        {
            await writer.WriteLineAsync(operation.rawJson).ConfigureAwait(false);
        }

        await writer.FlushAsync(ct).ConfigureAwait(false);

        // Write to file (create or append)
        stream.Position = 0;
        using var fileStream = new FileStream(
            path,
            append ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            useAsync: true);
        await stream.CopyToAsync(fileStream, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets all metadata file paths for a given resource type.
    /// Used by IndexLoaderService to scan on startup.
    /// </summary>
    public IEnumerable<string> GetAllMetadataFiles(string? resourceType = null)
    {
        // New metadata location: _internal/ResourceType/[resourceid]/*.metadata.json
        string searchDir = resourceType != null
            ? Path.Combine(_baseDirectory, "_internal", resourceType)
            : Path.Combine(_baseDirectory, "_internal");

        if (!Directory.Exists(searchDir))
        {
            return Enumerable.Empty<string>();
        }

        return Directory.GetFiles(searchDir, "*.metadata.json", SearchOption.AllDirectories);
    }

    /// <summary>
    /// Loads resource metadata with search indices for all resources of a given type.
    /// Used by FileBasedSearchService to enable search filtering before loading full resources.
    /// Only loads the LATEST version of each resource (not historical versions).
    /// </summary>
    /// <param name="resourceType">Resource type to load metadata for</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Collection of (ResourceKey, SearchIndexEntries) tuples - one per resource ID</returns>
    public async ValueTask<IReadOnlyList<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)>> GetResourceMetadataAsync(
        string resourceType,
        CancellationToken ct = default)
    {
        var results = new List<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)>();

        // Get resource type directory (_internal/ResourceType/)
        string resourceTypeDir = Path.Combine(_baseDirectory, "_internal", resourceType);
        if (!Directory.Exists(resourceTypeDir))
        {
            _logger.LogDebug("No metadata directory found for resource type {ResourceType}", resourceType);
            return results;
        }

        // Get all resource ID directories (_internal/ResourceType/[resourceid]/)
        var resourceIdDirs = Directory.GetDirectories(resourceTypeDir, "*", SearchOption.TopDirectoryOnly);

        _logger.LogDebug(
            "Found {Count} resource directories for type {ResourceType}",
            resourceIdDirs.Length,
            resourceType);

        // For each resource ID directory, find the latest metadata file
        foreach (var resourceIdDir in resourceIdDirs)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            try
            {
                // Get all metadata files for this resource ID
                var metadataFiles = Directory.GetFiles(resourceIdDir, "*.metadata.json", SearchOption.TopDirectoryOnly);

                if (metadataFiles.Length == 0)
                {
                    continue;
                }

                // Find the latest metadata file based on LastModified timestamp
                string? latestFile = null;
                DateTimeOffset latestTimestamp = DateTimeOffset.MinValue;

                foreach (var file in metadataFiles)
                {
                    try
                    {
                        var metadata = await ReadMetadataFileAsync(file, ct).ConfigureAwait(false);
                        if (metadata.LastModified > latestTimestamp)
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

                // Load the latest metadata file
                if (latestFile != null)
                {
                    var latestMetadata = await ReadMetadataFileAsync(latestFile, ct).ConfigureAwait(false);

                    var key = new ResourceKey(latestMetadata.ResourceType, latestMetadata.ResourceId, latestMetadata.VersionId);
                    var searchIndices = latestMetadata.SearchIndexes ?? new List<SearchIndexEntry>();

                    results.Add((key, searchIndices));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to load latest metadata from directory {Directory}",
                    resourceIdDir);
            }
        }

        _logger.LogDebug(
            "Loaded metadata for {Count} {ResourceType} resources (latest versions only)",
            results.Count,
            resourceType);

        return results;
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
        public List<SearchIndexEntry>? SearchIndexes { get; set; } = new List<SearchIndexEntry>();
    }

    private class TransactionManifest
    {
        public string TransactionId { get; set; } = string.Empty;
        public DateTimeOffset Timestamp { get; set; }
        public int OperationCount { get; set; }
        public List<ManifestOperation> Operations { get; set; } = new List<ManifestOperation>();
    }

    private class ManifestOperation
    {
        public int Index { get; set; }
        public string ResourceType { get; set; } = string.Empty;
        public string ResourceId { get; set; } = string.Empty;
    }
}
