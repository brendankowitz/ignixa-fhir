using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ignixa.Domain.Abstractions;

namespace Ignixa.DataLayer.BlobStorage;

/// <summary>
/// Local filesystem implementation of <see cref="IBlobStorageClient"/>.
/// Stores blobs as files in a configurable root directory.
/// </summary>
public class LocalFileBlobClient : IBlobStorageClient
{
    private readonly string _rootDirectory;
    private readonly ILogger<LocalFileBlobClient> _logger;

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

        _logger.LogDebug("Writing blob to {Path}", fullPath);

        await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
        await content.CopyToAsync(fileStream, cancellationToken);

        _logger.LogInformation("Successfully wrote blob to {Path} ({Bytes} bytes)", fullPath, fileStream.Length);
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

        _logger.LogDebug("Appending to blob at {Path}", fullPath);

        // Open in Append mode - creates file if it doesn't exist, appends if it does
        await using var fileStream = new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
        await content.CopyToAsync(fileStream, cancellationToken);

        _logger.LogInformation("Successfully appended to blob at {Path} ({Bytes} bytes appended)", fullPath, content.Length);
    }

    /// <inheritdoc/>
    public Task<Stream> ReadBlobAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(path);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Blob not found: {path}", fullPath);
        }

        _logger.LogDebug("Reading blob from {Path}", fullPath);

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
            _logger.LogInformation("Deleted blob at {Path}", fullPath);
        }
        else
        {
            _logger.LogWarning("Attempted to delete non-existent blob: {Path}", fullPath);
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

        _logger.LogDebug("Listed {Count} blobs under prefix {Prefix}", files.Count, pathPrefix);

        return Task.FromResult(files);
    }

    /// <inheritdoc/>
    public Task<string> GetBlobUrlAsync(string path, TimeSpan? expiresIn = null, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(path);

        // For local filesystem, return a file:// URL
        // In production (Azure Blob), this would return a SAS URL with expiration
        var fileUri = new Uri(fullPath).AbsoluteUri;

        _logger.LogDebug("Generated blob URL for {Path}: {Url}", path, fileUri);

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
