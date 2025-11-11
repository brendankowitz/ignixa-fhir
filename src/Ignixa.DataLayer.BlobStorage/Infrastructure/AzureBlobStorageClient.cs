using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ignixa.Domain.Abstractions;

namespace Ignixa.DataLayer.BlobStorage.Infrastructure;

/// <summary>
/// Azure Blob Storage implementation of <see cref="IBlobStorageClient"/>.
/// Uses Azure Blob Storage for cloud-based blob storage with SAS URL generation for access.
/// Supports multiple containers within the same storage account (container-agnostic paths).
/// </summary>
public partial class AzureBlobStorageClient : IBlobStorageClient
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly BlobContainerClient _defaultContainerClient;
    private readonly ILogger<AzureBlobStorageClient> _logger;
    private readonly AzureBlobStorageOptions _options;

    // Cache for container clients keyed by container name (supports multi-container scenarios)
    private readonly Dictionary<string, BlobContainerClient> _containerClientCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheSync = new();

    // Tracks which containers have been initialized (lazy initialization on first use)
    private readonly HashSet<string> _initializedContainers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _initSync = new();

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Writing blob to {Path}")]
        public static partial void WritingBlob(ILogger logger, string path);

        [LoggerMessage(Level = LogLevel.Information, Message = "Successfully wrote blob to {Path}")]
        public static partial void SuccessfullyWroteBlob(ILogger logger, string path);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Appending to blob at {Path}")]
        public static partial void AppendingToBlob(ILogger logger, string path);

        [LoggerMessage(Level = LogLevel.Information, Message = "Successfully appended to blob at {Path}")]
        public static partial void SuccessfullyAppendedToBlob(ILogger logger, string path);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Reading blob from {Path}")]
        public static partial void ReadingBlob(ILogger logger, string path);

        [LoggerMessage(Level = LogLevel.Information, Message = "Deleted blob at {Path}")]
        public static partial void DeletedBlob(ILogger logger, string path);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Attempted to delete non-existent blob: {Path}")]
        public static partial void AttemptedDeleteNonExistentBlob(ILogger logger, string path);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Listed {Count} blobs under prefix {Prefix}")]
        public static partial void ListedBlobs(ILogger logger, int count, string prefix);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Generated blob URL for {Path}")]
        public static partial void GeneratedBlobUrl(ILogger logger, string path);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureBlobStorageClient"/> class.
    /// </summary>
    /// <param name="blobServiceClient">Azure Blob Service client.</param>
    /// <param name="options">Configuration options containing default container name.</param>
    /// <param name="logger">Logger instance.</param>
    public AzureBlobStorageClient(
        BlobServiceClient blobServiceClient,
        IOptions<AzureBlobStorageOptions> options,
        ILogger<AzureBlobStorageClient> logger)
    {
        _blobServiceClient = blobServiceClient ?? throw new ArgumentNullException(nameof(blobServiceClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var containerName = _options.ContainerName
            ?? throw new ArgumentException("ContainerName must be specified in AzureBlobStorageOptions", nameof(options));

        _defaultContainerClient = blobServiceClient.GetBlobContainerClient(containerName);

        // Pre-populate cache with default container
        lock (_cacheSync)
        {
            _containerClientCache[containerName] = _defaultContainerClient;
        }
    }

    /// <summary>
    /// Gets or creates a BlobContainerClient for the specified container name.
    /// Supports lazy-loading of container clients for multi-container scenarios.
    /// </summary>
    private BlobContainerClient GetContainerClient(string containerName)
    {
        if (string.IsNullOrEmpty(containerName))
        {
            return _defaultContainerClient;
        }

        lock (_cacheSync)
        {
            if (_containerClientCache.TryGetValue(containerName, out var client))
            {
                return client;
            }

            // Create new container client and cache it
            var newClient = _blobServiceClient.GetBlobContainerClient(containerName);
            _containerClientCache[containerName] = newClient;
            _logger.LogDebug("Created container client for container: {ContainerName}", containerName);
            return newClient;
        }
    }

    /// <summary>
    /// Ensures the specified container exists, with lazy initialization on first use.
    /// Defers container creation to first actual blob operation to avoid blocking startup.
    /// </summary>
    private Task EnsureContainerExistsAsync(string containerName, CancellationToken cancellationToken)
    {
        // Fast path: container already initialized
        if (_initializedContainers.Contains(containerName))
        {
            return Task.CompletedTask;
        }

        // Slow path: initialize container (synchronous inside lock, then return Task)
        lock (_initSync)
        {
            // Double-check after acquiring lock
            if (_initializedContainers.Contains(containerName))
            {
                return Task.CompletedTask;
            }

            try
            {
                _logger.LogDebug("Ensuring container '{ContainerName}' exists (first use)", containerName);
                var containerClient = GetContainerClient(containerName);

                // Use synchronous CreateIfNotExists inside lock to avoid async in lock
#pragma warning disable CA1849 // Intentionally synchronous to avoid async inside lock
                containerClient.CreateIfNotExists(cancellationToken: cancellationToken);
#pragma warning restore CA1849

                _initializedContainers.Add(containerName);
                _logger.LogInformation("Container '{ContainerName}' initialized successfully", containerName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to initialize container '{ContainerName}'. Container may already exist or you may lack permissions. Will retry on next operation.",
                    containerName);

                // Still mark as attempted to avoid retrying on every operation
                _initializedContainers.Add(containerName);
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Parses a blob path to extract container name and blob name.
    /// Supports both:
    /// - Full blob URLs: https://account.blob.core.windows.net/container/blob/path
    /// - Container-qualified paths: container/blob/path
    /// - Blob-only paths (uses default container): blob/path
    /// </summary>
    private (string containerName, string blobName) ParseBlobPath(string path)
    {
        // Try to parse as a URL first
        if ((path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
            Uri.TryCreate(path, UriKind.Absolute, out var uri))
        {
            try
            {
                // Use Azure SDK's BlobUriBuilder for proper URL parsing
                var blobUri = new BlobUriBuilder(uri);
                return (blobUri.BlobContainerName, blobUri.BlobName);
            }
            catch
            {
                // Fall through to path-based parsing
            }
        }

        // Parse as container-qualified or blob-only path
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
        {
            // No container specified, use default
            return (_options.ContainerName ?? string.Empty, path);
        }

        // First part is container, rest is blob path
        var container = parts[0];
        var blob = string.Join("/", parts.Skip(1));

        return (container, blob);
    }

    /// <inheritdoc/>
    public async Task WriteBlobAsync(string path, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(content);

        Log.WritingBlob(_logger, path);

        var (containerName, blobName) = ParseBlobPath(path);
        await EnsureContainerExistsAsync(containerName, cancellationToken).ConfigureAwait(false);

        var containerClient = GetContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(content, overwrite: true, cancellationToken).ConfigureAwait(false);

        Log.SuccessfullyWroteBlob(_logger, path);
    }

    /// <inheritdoc/>
    public async Task AppendBlobAsync(string path, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(content);

        Log.AppendingToBlob(_logger, path);

        var (containerName, blobName) = ParseBlobPath(path);
        await EnsureContainerExistsAsync(containerName, cancellationToken).ConfigureAwait(false);

        var containerClient = GetContainerClient(containerName);
        var appendBlobClient = containerClient.GetAppendBlobClient(blobName);

        // Create append blob if it doesn't exist
        try
        {
            await appendBlobClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 409)
        {
            // Blob already exists as a different type, handle gracefully
        }

        // Append the content
        await appendBlobClient.AppendBlockAsync(content, cancellationToken: cancellationToken).ConfigureAwait(false);

        Log.SuccessfullyAppendedToBlob(_logger, path);
    }

    /// <inheritdoc/>
    public async Task<Stream> ReadBlobAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        Log.ReadingBlob(_logger, path);

        var (containerName, blobName) = ParseBlobPath(path);
        await EnsureContainerExistsAsync(containerName, cancellationToken).ConfigureAwait(false);

        var containerClient = GetContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);
        var download = await blobClient.DownloadAsync(cancellationToken).ConfigureAwait(false);

        return download.Value.Content;
    }

    /// <inheritdoc/>
    public async Task DeleteBlobAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var (containerName, blobName) = ParseBlobPath(path);
        await EnsureContainerExistsAsync(containerName, cancellationToken).ConfigureAwait(false);

        var containerClient = GetContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);

        var response = await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        if (response.Value)
        {
            Log.DeletedBlob(_logger, path);
        }
        else
        {
            Log.AttemptedDeleteNonExistentBlob(_logger, path);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> BlobExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var (containerName, blobName) = ParseBlobPath(path);
        await EnsureContainerExistsAsync(containerName, cancellationToken).ConfigureAwait(false);

        var containerClient = GetContainerClient(containerName);

        var blobClient = containerClient.GetBlobClient(blobName);
        var exists = await blobClient.ExistsAsync(cancellationToken).ConfigureAwait(false);

        return exists.Value;
    }

    /// <inheritdoc/>
    public async Task<List<string>> ListBlobsAsync(string pathPrefix, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(pathPrefix);

        var blobs = new List<string>();

        var (containerName, blobPrefix) = ParseBlobPath(pathPrefix);
        await EnsureContainerExistsAsync(containerName, cancellationToken).ConfigureAwait(false);

        var containerClient = GetContainerClient(containerName);

        await foreach (var blobItem in containerClient.GetBlobsAsync(
            prefix: blobPrefix,
            cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            blobs.Add(blobItem.Name);
        }

        Log.ListedBlobs(_logger, blobs.Count, pathPrefix);

        return blobs;
    }

    /// <inheritdoc/>
    public Task<string> GetBlobUrlAsync(string path, TimeSpan? expiresIn = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var (containerName, blobName) = ParseBlobPath(path);
        var containerClient = GetContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);

        // If expiration is specified, generate a SAS URL
        if (expiresIn.HasValue)
        {
            // Default BlobSasPermissions allows read access
            var sasUri = blobClient.GenerateSasUri(BlobSasPermissions.Read, DateTime.UtcNow.Add(expiresIn.Value));
            Log.GeneratedBlobUrl(_logger, path);
            return Task.FromResult(sasUri.ToString());
        }

        // Otherwise return the direct blob URI
        Log.GeneratedBlobUrl(_logger, path);
        return Task.FromResult(blobClient.Uri.ToString());
    }
}

