// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Ignixa.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Serialization;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.DataLayer.FileSystem.FileSystem;

/// <summary>
/// File-based FHIR repository implementation for prototype.
/// Stores resources as NDJSON files with metadata in sidecar .metadata.ndjson files.
/// </summary>
/// <remarks>
/// Directory structure: {baseDir}/{resourceType}/{YYYY}/{MM}/{DD}/tx-{transactionId}.ndjson
/// Metadata: {baseDir}/{resourceType}/{YYYY}/{MM}/{DD}/tx-{transactionId}.metadata.ndjson
/// </remarks>
public sealed partial class FileBasedFhirRepository : IFhirRepository, IDisposable
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

    public async ValueTask<SearchEntryResult?> GetAsync(ResourceKey key, CancellationToken ct = default)
    {
        try
        {
            // Find the latest metadata file for this resource
            var metadataFile = await FindLatestMetadataFileAsync(key, ct).ConfigureAwait(false);
            if (metadataFile == null)
            {
                LogResourceNotFound(_logger, key.ResourceType, key.Id);
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

            // Convert to UTF-8 bytes for zero-copy serialization (no parsing!)
            byte[] resourceJsonBytes = Encoding.UTF8.GetBytes(resourceJson);

            // Return SearchEntryResult with raw bytes for zero-copy serialization
            var result = new SearchEntryResult(
                ResourceType: key.ResourceType,
                ResourceId: key.Id,
                VersionId: metadata.VersionId,
                LastModified: metadata.LastModified,
                ResourceBytes: new ReadOnlyMemory<byte>(resourceJsonBytes))
            {
                IsDeleted = metadata.IsDeleted,
                TenantId = key.TenantId,
                Request = metadata.Request
            };

            LogResourceRetrieved(_logger, key.ResourceType, key.Id, metadata.VersionId);

            return result;
        }
        catch (Exception ex)
        {
            LogFailedToReadResource(_logger, ex, key.ResourceType, key.Id);
            throw;
        }
    }

    public async ValueTask<UpdateResult> CreateOrUpdateAsync(ResourceWrapper resource, CancellationToken ct = default)
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
            string resourceJson = resource.Resource.SerializeToString();

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

            LogResourceStored(_logger, resource.ResourceType, resource.ResourceId, metadata.VersionId, transactionId);

            // Return UpdateResult with ResourceKey + raw bytes (only deserialize if needed)
            var resourceBytes = System.Text.Encoding.UTF8.GetBytes(resourceJson);
            var resultKey = new ResourceKey(
                ResourceType: resource.ResourceType,
                Id: resource.ResourceId,
                VersionId: metadata.VersionId);

            return new UpdateResult(
                Key: resultKey,
                ResourceBytes: resourceBytes,
                LastModified: timestamp)
            {
                Request = resource.Request
            };
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask<ResourceKey?> DeleteAsync(
        ResourceKey key,
        ResourceRequest request,
        TransactionId? transactionId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(request);

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            LogDeletingResource(_logger, key.ResourceType, key.Id);

            // Check if resource exists
            var metadataFile = await FindLatestMetadataFileAsync(key, ct).ConfigureAwait(false);
            if (metadataFile == null)
            {
                // Resource never existed - return null (404 Not Found)
                LogCannotDeleteNonExistent(_logger, key.ResourceType, key.Id);
                return null;
            }

            // Read current metadata
            var currentMetadata = await ReadMetadataFileAsync(metadataFile, ct).ConfigureAwait(false);

            // Check if already deleted (idempotency)
            if (currentMetadata.IsDeleted)
            {
                LogResourceAlreadyDeleted(_logger, key.ResourceType, key.Id, currentMetadata.VersionId);

                return new ResourceKey(
                    key.ResourceType,
                    key.Id,
                    currentMetadata.VersionId,
                    key.TenantId);
            }

            // Create new deleted version (tombstone)
            var txId = transactionId ?? TransactionId.Generate();
            var timestamp = DateTimeOffset.UtcNow;
            int newVersion = int.Parse(currentMetadata.VersionId) + 1;

            // Create minimal tombstone JSON (FHIR spec: only id and meta)
            var tombstoneJson = $"{{\"resourceType\":\"{key.ResourceType}\",\"id\":\"{key.Id}\",\"meta\":{{\"versionId\":\"{newVersion}\",\"lastUpdated\":\"{timestamp:o}\"}}}}";

            // Get date-based directory path
            string dateDirectory = GetDateDirectory(key.ResourceType, timestamp);
            Directory.CreateDirectory(dateDirectory);

            // Write tombstone NDJSON file
            string ndjsonPath = Path.Combine(dateDirectory, $"tx-{txId}.ndjson");
            await File.WriteAllTextAsync(ndjsonPath, tombstoneJson, ct).ConfigureAwait(false);

            // Write metadata sidecar with IsDeleted = true
            string internalMetadataDir = Path.Combine(
                _baseDirectory,
                "_internal",
                key.ResourceType,
                key.Id);
            Directory.CreateDirectory(internalMetadataDir);

            string internalMetadataPath = Path.Combine(internalMetadataDir, $"{txId}.metadata.json");

            var deletedMetadata = new ResourceMetadata
            {
                TransactionId = txId.ToString(),
                ResourceType = key.ResourceType,
                ResourceId = key.Id,
                VersionId = newVersion.ToString(),
                LastModified = timestamp,
                IsDeleted = true, // Mark as deleted
                Request = request,
                SearchIndexes = null, // Deleted resources have no search indices
            };

            string metadataJson = JsonSerializer.Serialize(deletedMetadata, _jsonOptions);
            await File.WriteAllTextAsync(internalMetadataPath, metadataJson, ct).ConfigureAwait(false);

            var resultKey = new ResourceKey(key.ResourceType, key.Id, newVersion.ToString(), key.TenantId);

            LogResourceDeleted(_logger, key.ResourceType, key.Id, newVersion);

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
        string? resourceJson;
        while ((resourceJson = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
        {
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
            LogLockFileNotFound(_logger, transactionId, lockFilePath);
            return;
        }

        // Rename lock file to committed file
        File.Move(lockFilePath, committedFilePath);

        LogTransactionCommitted(_logger, transactionId, committedFilePath);

        await ValueTask.CompletedTask;
    }

    public async ValueTask<IReadOnlyList<TransactionId>> GetStalledTransactionsAsync(
        TimeSpan stallThreshold,
        CancellationToken ct = default)
    {
        var stalledTransactions = new List<TransactionId>();

        // Get the _transactions directory
        string transactionsDir = Path.Combine(_baseDirectory, "_transactions");
        if (!Directory.Exists(transactionsDir))
        {
            LogNoTransactionsDirectory(_logger, transactionsDir);
            return stalledTransactions;
        }

        // Get all .lock.ndjson files recursively
        var lockFiles = Directory.GetFiles(transactionsDir, "*.lock.ndjson", SearchOption.AllDirectories);

        var threshold = DateTimeOffset.UtcNow - stallThreshold;

        LogScanningLockFiles(_logger, lockFiles.Length, threshold);

        foreach (var lockFile in lockFiles)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            try
            {
                // Get file last write time
                var fileInfo = new FileInfo(lockFile);
                var lastWriteTime = new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero);

                // Check if file is older than threshold
                if (lastWriteTime < threshold)
                {
                    // Extract transaction ID from filename (format: tx-{transactionId}.lock.ndjson)
                    var fileName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(lockFile)); // Remove .lock.ndjson
                    if (fileName.StartsWith("tx-", StringComparison.Ordinal))
                    {
                        var transactionIdString = fileName.Substring(3); // Remove "tx-" prefix
                        if (TransactionId.TryParse(transactionIdString, out var transactionId))
                        {
                            stalledTransactions.Add(transactionId);

                            var age = DateTimeOffset.UtcNow - lastWriteTime;
                            LogFoundStalledTransaction(_logger, transactionId, lockFile, age);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogFailedToProcessLockFile(_logger, ex, lockFile);
            }
        }

        LogStalledTransactionsSummary(_logger, stalledTransactions.Count, lockFiles.Length);

        return await ValueTask.FromResult(stalledTransactions);
    }

    public async Task<IReadOnlyList<ResourceKey>> BatchWriteAsync(
        TransactionId transactionId,
        IReadOnlyList<(string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes, string httpMethod, int entryIndex)> operations,
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

            LogBatchWriteStarting(_logger, operations.Count, transactionId);

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

            LogGroupedOperations(_logger, operations.Count, operationsByType.Count);

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
                    Request = new ResourceRequest(operation.httpMethod, $"{operation.resourceType}/{operation.resourceId}"),
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
                LogAppendedToLockFile(_logger, lockFilePath);
            }
            else
            {
                LogCreatedLockFile(_logger, lockFilePath);
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
                    LogAppendingResources(_logger, typeOperations.Count, resourceFilePath);
                }
                else
                {
                    LogCreatingResourceFile(_logger, typeOperations.Count, resourceFilePath);
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

                LogMetadataWritten(_logger, metadataPath);
            }

            // Step 8: Batch complete - lock file remains until transaction is committed externally
            LogBatchWriteCompleted(_logger, operations.Count, transactionId);

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
        IReadOnlyList<(string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes, string httpMethod, int entryIndex)> operations,
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
        List<(string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes, string httpMethod, int entryIndex)> operations,
        bool append,
        CancellationToken ct)
    {
        using var stream = _memoryStreamManager.GetStream("resource-file-write");
        using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);

        // Write resource JSON (one per line, no bundle header)
        // Transaction metadata is stored in /transactions files
        foreach (var operation in operations)
        {
            // Serialize ResourceJsonNode to JSON string
            string rawJson = JsonSerializer.Serialize(operation.resource, _jsonOptions);
            await writer.WriteLineAsync(rawJson).ConfigureAwait(false);
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
            LogNoMetadataDirectory(_logger, resourceType);
            return results;
        }

        // Get all resource ID directories (_internal/ResourceType/[resourceid]/)
        var resourceIdDirs = Directory.GetDirectories(resourceTypeDir, "*", SearchOption.TopDirectoryOnly);

        LogFoundResourceDirectories(_logger, resourceIdDirs.Length, resourceType);

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
                LogFailedToLoadMetadata(_logger, ex, resourceIdDir);
            }
        }

        LogMetadataLoaded(_logger, results.Count, resourceType);

        return results;
    }

    public async IAsyncEnumerable<SearchEntryResult> GetResourceHistoryAsync(
        ResourceKey key,
        HistoryQueryParameters parameters,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Validate parameters
        parameters = parameters.Validate();

        // Get metadata directory for this specific resource
        string metadataDir = Path.Combine(_baseDirectory, "_internal", key.ResourceType, key.Id);
        if (!Directory.Exists(metadataDir))
        {
            LogNoHistoryForResource(_logger, key.ResourceType, key.Id);
            yield break;
        }

        // Get all metadata files for this resource (all versions)
        var metadataFiles = Directory.GetFiles(metadataDir, "*.metadata.json", SearchOption.TopDirectoryOnly);
        var allMetadata = new List<ResourceMetadata>();

        foreach (var file in metadataFiles)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var metadata = await ReadMetadataFileAsync(file, ct).ConfigureAwait(false);
                allMetadata.Add(metadata);
            }
            catch (Exception ex)
            {
                LogFailedToReadMetadataFile(_logger, ex, file);
            }
        }

        // Apply filters and sorting
        var filtered = ApplyHistoryFilters(allMetadata, parameters);

        // Skip offset, then yield up to count results
        int skipped = 0;
        int returned = 0;

        foreach (var metadata in filtered)
        {
            ct.ThrowIfCancellationRequested();

            // Skip offset entries
            if (skipped < parameters.Offset)
            {
                skipped++;
                continue;
            }

            // Stop after count entries
            if (returned >= parameters.Count)
            {
                break;
            }

            // Load resource and yield
            SearchEntryResult? result = await LoadResourceVersionAsync(metadata, ct).ConfigureAwait(false);
            if (result != null)
            {
                returned++;
                yield return result;
            }
        }

        LogResourceHistoryResult(_logger, key.ResourceType, key.Id, returned);
    }

    public async IAsyncEnumerable<SearchEntryResult> GetTypeHistoryAsync(
        string resourceType,
        int tenantId,
        HistoryQueryParameters parameters,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Validate parameters
        parameters = parameters.Validate();

        // Get all metadata files for this resource type
        string resourceTypeDir = Path.Combine(_baseDirectory, "_internal", resourceType);
        if (!Directory.Exists(resourceTypeDir))
        {
            LogNoHistoryForType(_logger, resourceType);
            yield break;
        }

        // Scan all resource ID directories for metadata files
        var allMetadata = new List<ResourceMetadata>();
        var resourceIdDirs = Directory.GetDirectories(resourceTypeDir, "*", SearchOption.TopDirectoryOnly);

        foreach (var resourceIdDir in resourceIdDirs)
        {
            ct.ThrowIfCancellationRequested();

            var metadataFiles = Directory.GetFiles(resourceIdDir, "*.metadata.json", SearchOption.TopDirectoryOnly);
            foreach (var file in metadataFiles)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var metadata = await ReadMetadataFileAsync(file, ct).ConfigureAwait(false);
                    allMetadata.Add(metadata);
                }
                catch (Exception ex)
                {
                    LogFailedToReadMetadataFile(_logger, ex, file);
                }
            }
        }

        // Apply filters and sorting
        var filtered = ApplyHistoryFilters(allMetadata, parameters);

        // Skip offset, then yield up to count results
        int skipped = 0;
        int returned = 0;

        foreach (var metadata in filtered)
        {
            ct.ThrowIfCancellationRequested();

            // Skip offset entries
            if (skipped < parameters.Offset)
            {
                skipped++;
                continue;
            }

            // Stop after count entries
            if (returned >= parameters.Count)
            {
                break;
            }

            // Load resource and yield
            SearchEntryResult? result = await LoadResourceVersionAsync(metadata, ct).ConfigureAwait(false);
            if (result != null)
            {
                returned++;
                yield return result;
            }
        }

        LogTypeHistoryResult(_logger, resourceType, returned);
    }

    public async IAsyncEnumerable<SearchEntryResult> GetSystemHistoryAsync(
        int tenantId,
        HistoryQueryParameters parameters,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Validate parameters
        parameters = parameters.Validate();

        // Get all metadata files across all resource types
        string internalDir = Path.Combine(_baseDirectory, "_internal");
        if (!Directory.Exists(internalDir))
        {
            LogNoSystemHistory(_logger);
            yield break;
        }

        // Scan all resource type directories
        var allMetadata = new List<ResourceMetadata>();
        var resourceTypeDirs = Directory.GetDirectories(internalDir, "*", SearchOption.TopDirectoryOnly);

        foreach (var resourceTypeDir in resourceTypeDirs)
        {
            ct.ThrowIfCancellationRequested();

            // Scan all resource ID directories
            var resourceIdDirs = Directory.GetDirectories(resourceTypeDir, "*", SearchOption.TopDirectoryOnly);
            foreach (var resourceIdDir in resourceIdDirs)
            {
                ct.ThrowIfCancellationRequested();

                var metadataFiles = Directory.GetFiles(resourceIdDir, "*.metadata.json", SearchOption.TopDirectoryOnly);
                foreach (var file in metadataFiles)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var metadata = await ReadMetadataFileAsync(file, ct).ConfigureAwait(false);
                        allMetadata.Add(metadata);
                    }
                    catch (Exception ex)
                    {
                        LogFailedToReadMetadataFile(_logger, ex, file);
                    }
                }
            }
        }

        // Apply filters and sorting
        var filtered = ApplyHistoryFilters(allMetadata, parameters);

        // Skip offset, then yield up to count results
        int skipped = 0;
        int returned = 0;

        foreach (var metadata in filtered)
        {
            ct.ThrowIfCancellationRequested();

            // Skip offset entries
            if (skipped < parameters.Offset)
            {
                skipped++;
                continue;
            }

            // Stop after count entries
            if (returned >= parameters.Count)
            {
                break;
            }

            // Load resource and yield
            SearchEntryResult? result = await LoadResourceVersionAsync(metadata, ct).ConfigureAwait(false);
            if (result != null)
            {
                returned++;
                yield return result;
            }
        }

        LogSystemHistoryResult(_logger, returned);
    }

    /// <summary>
    /// Applies history filters (timestamp range) and sorting to metadata list.
    /// Does NOT apply pagination - caller handles offset/limit during iteration.
    /// </summary>
    private static IEnumerable<ResourceMetadata> ApplyHistoryFilters(
        List<ResourceMetadata> allMetadata,
        HistoryQueryParameters parameters)
    {
        // Filter by timestamp range (_since and _until)
        var filtered = allMetadata.AsEnumerable();

        if (parameters.Since.HasValue)
        {
            filtered = filtered.Where(m => m.LastModified >= parameters.Since.Value);
        }

        if (parameters.Until.HasValue)
        {
            filtered = filtered.Where(m => m.LastModified <= parameters.Until.Value);
        }

        // Sort by LastModified (ascending or descending)
        filtered = parameters.Sort == HistorySortOrder.Ascending
            ? filtered.OrderBy(m => m.LastModified)
            : filtered.OrderByDescending(m => m.LastModified);

        return filtered;
    }

    /// <summary>
    /// Loads a single resource version from disk given its metadata.
    /// Returns null if resource file cannot be loaded.
    /// </summary>
    private async ValueTask<SearchEntryResult?> LoadResourceVersionAsync(
        ResourceMetadata metadata,
        CancellationToken ct)
    {
        try
        {
            // Build path to resource NDJSON file
            string resourceTypeDir = GetDateDirectory(metadata.ResourceType, metadata.LastModified);
            string ndjsonPath = Path.Combine(resourceTypeDir, $"tx-{metadata.TransactionId}.ndjson");

            // Read resource JSON from NDJSON file
            string resourceJson = await ReadResourceFromNdjsonByIdAsync(ndjsonPath, metadata.ResourceId, ct).ConfigureAwait(false);

            // Convert to bytes for zero-copy serialization
            byte[] resourceJsonBytes = Encoding.UTF8.GetBytes(resourceJson);

            // Create SearchEntryResult
            return new SearchEntryResult(
                ResourceType: metadata.ResourceType,
                ResourceId: metadata.ResourceId,
                VersionId: metadata.VersionId,
                LastModified: metadata.LastModified,
                ResourceBytes: new ReadOnlyMemory<byte>(resourceJsonBytes))
            {
                IsDeleted = metadata.IsDeleted,
                Request = metadata.Request
            };
        }
        catch (Exception ex)
        {
            LogFailedToLoadResourceVersion(_logger, ex, metadata.ResourceType, metadata.ResourceId, metadata.VersionId);
            return null;
        }
    }

    public Task<IReadOnlyList<ExpiredResourceInfo>> GetExpiredResourcesAsync(
        int batchSize,
        CancellationToken ct = default)
    {
        LogExpiredResourcesNotImplemented(_logger);
        return Task.FromResult<IReadOnlyList<ExpiredResourceInfo>>(Array.Empty<ExpiredResourceInfo>());
    }

    public Task HardDeleteResourceAsync(
        short resourceTypeId,
        string resourceId,
        CancellationToken ct = default)
    {
        LogHardDeleteNotImplemented(_logger);
        return Task.CompletedTask;
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

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Resource not found: {ResourceType}/{Id}")]
    private static partial void LogResourceNotFound(ILogger logger, string resourceType, string id);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Retrieved resource: {ResourceType}/{Id} version {VersionId}")]
    private static partial void LogResourceRetrieved(ILogger logger, string resourceType, string id, string versionId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Failed to read resource: {ResourceType}/{Id}")]
    private static partial void LogFailedToReadResource(ILogger logger, Exception ex, string resourceType, string id);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Stored resource: {ResourceType}/{Id} version {VersionId} tx {TransactionId}")]
    private static partial void LogResourceStored(ILogger logger, string resourceType, string id, string versionId, TransactionId transactionId);

    [LoggerMessage(EventId = 5, Level = LogLevel.Debug, Message = "Deleting resource {ResourceType}/{Id}")]
    private static partial void LogDeletingResource(ILogger logger, string resourceType, string id);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Cannot delete {ResourceType}/{Id}: resource never existed")]
    private static partial void LogCannotDeleteNonExistent(ILogger logger, string resourceType, string id);

    [LoggerMessage(EventId = 7, Level = LogLevel.Debug, Message = "Resource {ResourceType}/{Id} already deleted at version {Version} (idempotent)")]
    private static partial void LogResourceAlreadyDeleted(ILogger logger, string resourceType, string id, string version);

    [LoggerMessage(EventId = 8, Level = LogLevel.Information, Message = "Deleted resource: {ResourceType}/{Id} (created tombstone version {Version})")]
    private static partial void LogResourceDeleted(ILogger logger, string resourceType, string id, int version);

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning, Message = "Lock file not found for transaction {TransactionId}: {LockFile}")]
    private static partial void LogLockFileNotFound(ILogger logger, TransactionId transactionId, string lockFile);

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "Transaction {TransactionId} committed: {CommittedFile}")]
    private static partial void LogTransactionCommitted(ILogger logger, TransactionId transactionId, string committedFile);

    [LoggerMessage(EventId = 11, Level = LogLevel.Debug, Message = "No _transactions directory found at {TransactionsDir}")]
    private static partial void LogNoTransactionsDirectory(ILogger logger, string transactionsDir);

    [LoggerMessage(EventId = 12, Level = LogLevel.Debug, Message = "Scanning {Count} lock files for stalled transactions (threshold: {Threshold})")]
    private static partial void LogScanningLockFiles(ILogger logger, int count, DateTimeOffset threshold);

    [LoggerMessage(EventId = 13, Level = LogLevel.Warning, Message = "Found stalled transaction {TransactionId} in file {LockFile} (age: {Age})")]
    private static partial void LogFoundStalledTransaction(ILogger logger, TransactionId transactionId, string lockFile, TimeSpan age);

    [LoggerMessage(EventId = 14, Level = LogLevel.Warning, Message = "Failed to process lock file {LockFile}")]
    private static partial void LogFailedToProcessLockFile(ILogger logger, Exception ex, string lockFile);

    [LoggerMessage(EventId = 15, Level = LogLevel.Debug, Message = "Found {Count} stalled transactions out of {TotalCount} lock files")]
    private static partial void LogStalledTransactionsSummary(ILogger logger, int count, int totalCount);

    [LoggerMessage(EventId = 16, Level = LogLevel.Information, Message = "Starting batch write of {Count} resources with transaction ID {TransactionId}")]
    private static partial void LogBatchWriteStarting(ILogger logger, int count, TransactionId transactionId);

    [LoggerMessage(EventId = 17, Level = LogLevel.Debug, Message = "Grouped {TotalCount} operations into {GroupCount} resource types")]
    private static partial void LogGroupedOperations(ILogger logger, int totalCount, int groupCount);

    [LoggerMessage(EventId = 18, Level = LogLevel.Debug, Message = "Appended to lock file: {LockFile}")]
    private static partial void LogAppendedToLockFile(ILogger logger, string lockFile);

    [LoggerMessage(EventId = 19, Level = LogLevel.Debug, Message = "Created lock file: {LockFile}")]
    private static partial void LogCreatedLockFile(ILogger logger, string lockFile);

    [LoggerMessage(EventId = 20, Level = LogLevel.Debug, Message = "Appending {Count} resources to existing file: {FilePath}")]
    private static partial void LogAppendingResources(ILogger logger, int count, string filePath);

    [LoggerMessage(EventId = 21, Level = LogLevel.Debug, Message = "Creating new file with {Count} resources: {FilePath}")]
    private static partial void LogCreatingResourceFile(ILogger logger, int count, string filePath);

    [LoggerMessage(EventId = 22, Level = LogLevel.Debug, Message = "Metadata written: {MetadataPath}")]
    private static partial void LogMetadataWritten(ILogger logger, string metadataPath);

    [LoggerMessage(EventId = 23, Level = LogLevel.Information, Message = "Batch write completed: {Count} resources written in transaction {TransactionId}")]
    private static partial void LogBatchWriteCompleted(ILogger logger, int count, TransactionId transactionId);

    [LoggerMessage(EventId = 24, Level = LogLevel.Debug, Message = "No metadata directory found for resource type {ResourceType}")]
    private static partial void LogNoMetadataDirectory(ILogger logger, string resourceType);

    [LoggerMessage(EventId = 25, Level = LogLevel.Debug, Message = "Found {Count} resource directories for type {ResourceType}")]
    private static partial void LogFoundResourceDirectories(ILogger logger, int count, string resourceType);

    [LoggerMessage(EventId = 26, Level = LogLevel.Warning, Message = "Failed to load latest metadata from directory {Directory}")]
    private static partial void LogFailedToLoadMetadata(ILogger logger, Exception ex, string directory);

    [LoggerMessage(EventId = 27, Level = LogLevel.Debug, Message = "Loaded metadata for {Count} {ResourceType} resources (latest versions only)")]
    private static partial void LogMetadataLoaded(ILogger logger, int count, string resourceType);

    [LoggerMessage(EventId = 28, Level = LogLevel.Debug, Message = "No history found for resource: {ResourceType}/{Id}")]
    private static partial void LogNoHistoryForResource(ILogger logger, string resourceType, string id);

    [LoggerMessage(EventId = 29, Level = LogLevel.Warning, Message = "Failed to read metadata file {File}")]
    private static partial void LogFailedToReadMetadataFile(ILogger logger, Exception ex, string file);

    [LoggerMessage(EventId = 30, Level = LogLevel.Debug, Message = "Resource history query: {ResourceType}/{Id} returned {Count} versions")]
    private static partial void LogResourceHistoryResult(ILogger logger, string resourceType, string id, int count);

    [LoggerMessage(EventId = 31, Level = LogLevel.Debug, Message = "No history found for resource type: {ResourceType}")]
    private static partial void LogNoHistoryForType(ILogger logger, string resourceType);

    [LoggerMessage(EventId = 32, Level = LogLevel.Debug, Message = "Type history query: {ResourceType} returned {Count} versions")]
    private static partial void LogTypeHistoryResult(ILogger logger, string resourceType, int count);

    [LoggerMessage(EventId = 33, Level = LogLevel.Debug, Message = "No history found in system")]
    private static partial void LogNoSystemHistory(ILogger logger);

    [LoggerMessage(EventId = 34, Level = LogLevel.Debug, Message = "System history query returned {Count} versions")]
    private static partial void LogSystemHistoryResult(ILogger logger, int count);

    [LoggerMessage(EventId = 35, Level = LogLevel.Warning, Message = "Failed to load resource version {ResourceType}/{Id} v{Version}")]
    private static partial void LogFailedToLoadResourceVersion(ILogger logger, Exception ex, string resourceType, string id, string version);

    [LoggerMessage(EventId = 36, Level = LogLevel.Warning, Message = "GetExpiredResourcesAsync not implemented for FileBasedFhirRepository - TTL cleanup not supported")]
    private static partial void LogExpiredResourcesNotImplemented(ILogger logger);

    [LoggerMessage(EventId = 37, Level = LogLevel.Warning, Message = "HardDeleteResourceAsync not implemented for FileBasedFhirRepository - TTL cleanup not supported")]
    private static partial void LogHardDeleteNotImplemented(ILogger logger);
}
