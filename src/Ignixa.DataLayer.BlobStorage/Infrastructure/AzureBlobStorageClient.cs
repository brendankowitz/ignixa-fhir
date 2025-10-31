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
/// </summary>
public partial class AzureBlobStorageClient : IBlobStorageClient
{
    private readonly BlobContainerClient _containerClient;
    private readonly ILogger<AzureBlobStorageClient> _logger;
    private readonly AzureBlobStorageOptions _options;

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
    /// <param name="options">Configuration options containing container name.</param>
    /// <param name="logger">Logger instance.</param>
    public AzureBlobStorageClient(
        BlobServiceClient blobServiceClient,
        IOptions<AzureBlobStorageOptions> options,
        ILogger<AzureBlobStorageClient> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var containerName = _options.ContainerName
            ?? throw new ArgumentException("ContainerName must be specified in AzureBlobStorageOptions", nameof(options));

        _containerClient = blobServiceClient.GetBlobContainerClient(containerName);
    }

    /// <inheritdoc/>
    public async Task WriteBlobAsync(string path, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(content);

        Log.WritingBlob(_logger, path);

        var blobClient = _containerClient.GetBlobClient(path);
        await blobClient.UploadAsync(content, overwrite: true, cancellationToken).ConfigureAwait(false);

        Log.SuccessfullyWroteBlob(_logger, path);
    }

    /// <inheritdoc/>
    public async Task AppendBlobAsync(string path, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(content);

        Log.AppendingToBlob(_logger, path);

        var appendBlobClient = _containerClient.GetAppendBlobClient(path);

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

        var blobClient = _containerClient.GetBlobClient(path);
        var download = await blobClient.DownloadAsync(cancellationToken).ConfigureAwait(false);

        return download.Value.Content;
    }

    /// <inheritdoc/>
    public async Task DeleteBlobAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var blobClient = _containerClient.GetBlobClient(path);

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

        var blobClient = _containerClient.GetBlobClient(path);
        var exists = await blobClient.ExistsAsync(cancellationToken).ConfigureAwait(false);

        return exists.Value;
    }

    /// <inheritdoc/>
    public async Task<List<string>> ListBlobsAsync(string pathPrefix, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(pathPrefix);

        var blobs = new List<string>();

        await foreach (var blobItem in _containerClient.GetBlobsAsync(
            prefix: pathPrefix,
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

        var blobClient = _containerClient.GetBlobClient(path);

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

