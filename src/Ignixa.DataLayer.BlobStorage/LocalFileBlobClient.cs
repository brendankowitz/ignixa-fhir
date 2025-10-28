using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ignixa.Domain.Abstractions;

namespace Ignixa.DataLayer.BlobStorage;

/// <summary>
/// Local filesystem implementation of <see cref="IBlobStorageClient"/>.
/// Stores blobs as files in a configurable root directory.
/// </summary>
public partial class LocalFileBlobClient : IBlobStorageClient
{
    private readonly string _rootDirectory;
    private readonly ILogger<LocalFileBlobClient> _logger;

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Writing blob to {Path}")]
        public static partial void WritingBlob(ILogger logger, string path);

        [LoggerMessage(Level = LogLevel.Information, Message = "Successfully wrote blob to {Path} ({Bytes} bytes)")]
        public static partial void SuccessfullyWroteBlob(ILogger logger, string path, long bytes);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Appending to blob at {Path}")]
        public static partial void AppendingToBlob(ILogger logger, string path);

        [LoggerMessage(Level = LogLevel.Information, Message = "Successfully appended to blob at {Path} ({Bytes} bytes appended)")]
        public static partial void SuccessfullyAppendedToBlob(ILogger logger, string path, long bytes);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Reading blob from {Path}")]
        public static partial void ReadingBlob(ILogger logger, string path);

        [LoggerMessage(Level = LogLevel.Information, Message = "Deleted blob at {Path}")]
        public static partial void DeletedBlob(ILogger logger, string path);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Attempted to delete non-existent blob: {Path}")]
        public static partial void AttemptedDeleteNonExistentBlob(ILogger logger, string path);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Listed {Count} blobs under prefix {Prefix}")]
        public static partial void ListedBlobs(ILogger logger, int count, string prefix);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Generated blob URL for {Path}: {Url}")]
        public static partial void GeneratedBlobUrl(ILogger logger, string path, string url);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalFileBlobClient"/> class.
    /// </summary>
    /// <param name="options">Configuration options containing the root directory.</param>
    /// <param name="logger">Logger instance.</param>
    public LocalFileBlobClient(
        IOptions<LocalFileBlobStorageOptions> options,
        ILogger<LocalFileBlobClient> logger)
    {
        _rootDirectory = options.Value.RootDirectory
            ?? throw new ArgumentException("RootDirectory must be specified in LocalFileBlobStorageOptions", nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Ensure root directory exists
        Directory.CreateDirectory(_rootDirectory);
    }

    /// <inheritdoc/>
    public async Task WriteBlobAsync(string path, Stream content, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);

        if (directory != null)
        {
            Directory.CreateDirectory(directory);
        }

        Log.WritingBlob(_logger, fullPath);

        var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
        await using (fileStream.ConfigureAwait(false))
        {
            await content.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);

            Log.SuccessfullyWroteBlob(_logger, fullPath, fileStream.Length);
        }
    }

    /// <inheritdoc/>
    public async Task AppendBlobAsync(string path, Stream content, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);

        if (directory != null)
        {
            Directory.CreateDirectory(directory);
        }

        Log.AppendingToBlob(_logger, fullPath);

        // Open in Append mode - creates file if it doesn't exist, appends if it does
        var fileStream = new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
        await using (fileStream.ConfigureAwait(false))
        {
            await content.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);

            Log.SuccessfullyAppendedToBlob(_logger, fullPath, content.Length);
        }
    }

    /// <inheritdoc/>
    public Task<Stream> ReadBlobAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(path);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Blob not found: {path}", fullPath);
        }

        Log.ReadingBlob(_logger, fullPath);

        Stream fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
        return Task.FromResult(fileStream);
    }

    /// <inheritdoc/>
    public Task DeleteBlobAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(path);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            Log.DeletedBlob(_logger, fullPath);
        }
        else
        {
            Log.AttemptedDeleteNonExistentBlob(_logger, fullPath);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> BlobExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(path);
        return Task.FromResult(File.Exists(fullPath));
    }

    /// <inheritdoc/>
    public Task<List<string>> ListBlobsAsync(string pathPrefix, CancellationToken cancellationToken = default)
    {
        var fullPrefix = GetFullPath(pathPrefix);
        var directory = Path.GetDirectoryName(fullPrefix) ?? _rootDirectory;

        if (!Directory.Exists(directory))
        {
            return Task.FromResult(new List<string>());
        }

        var searchPattern = Path.GetFileName(fullPrefix);
        if (string.IsNullOrEmpty(searchPattern))
        {
            searchPattern = "*";
        }

        var files = Directory.GetFiles(directory, searchPattern, SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(_rootDirectory, f).Replace('\\', '/'))
            .ToList();

        Log.ListedBlobs(_logger, files.Count, pathPrefix);

        return Task.FromResult(files);
    }

    /// <inheritdoc/>
    public Task<string> GetBlobUrlAsync(string path, TimeSpan? expiresIn = null, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(path);

        // For local filesystem, return a file:// URL
        // In production (Azure Blob), this would return a SAS URL with expiration
        var fileUri = new Uri(fullPath).AbsoluteUri;

        Log.GeneratedBlobUrl(_logger, path, fileUri);

        return Task.FromResult(fileUri);
    }

    private string GetFullPath(string relativePath)
    {
        // Normalize path separators
        var normalizedPath = relativePath.Replace('/', Path.DirectorySeparatorChar);

        // Combine with root directory
        var fullPath = Path.Combine(_rootDirectory, normalizedPath);

        // Ensure the resolved path is within the root directory (security check)
        var resolvedPath = Path.GetFullPath(fullPath);
        if (!resolvedPath.StartsWith(_rootDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Path traversal detected: {relativePath}");
        }

        return resolvedPath;
    }
}

/// <summary>
/// Configuration options for <see cref="LocalFileBlobClient"/>.
/// </summary>
public class LocalFileBlobStorageOptions
{
    /// <summary>
    /// Root directory where blobs are stored.
    /// Example: "C:/FhirData/exports" or "/var/fhir/exports"
    /// </summary>
    public string? RootDirectory { get; set; }
}
