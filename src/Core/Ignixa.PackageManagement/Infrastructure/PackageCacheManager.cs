using Microsoft.Extensions.Logging;

namespace Ignixa.PackageManagement.Infrastructure;

/// <summary>
/// Manages local filesystem caching of downloaded FHIR packages.
/// Prevents re-downloading packages and supports offline access.
/// </summary>
public class PackageCacheManager
{
    private readonly string _cacheDirectory;
    private readonly ILogger<PackageCacheManager> _logger;

    /// <summary>
    /// Initializes a new instance of the PackageCacheManager class.
    /// </summary>
    /// <param name="cacheDirectory">Directory to store cached packages</param>
    /// <param name="logger">Logger instance</param>
    public PackageCacheManager(string cacheDirectory, ILogger<PackageCacheManager> logger)
    {
        if (string.IsNullOrWhiteSpace(cacheDirectory))
            throw new ArgumentException("Cache directory cannot be null or empty", nameof(cacheDirectory));

        _cacheDirectory = cacheDirectory;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Create cache directory if it doesn't exist
        Directory.CreateDirectory(_cacheDirectory);
    }

    /// <summary>
    /// Gets the path where a package would be cached.
    /// </summary>
    /// <param name="packageId">Package ID (e.g., "hl7.fhir.us.core")</param>
    /// <param name="version">Package version (e.g., "5.0.1")</param>
    /// <returns>Full path to cached package file</returns>
    public string GetCachePath(string packageId, string version)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("Package ID cannot be null or empty", nameof(packageId));
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version cannot be null or empty", nameof(version));

        // Sanitize package ID for filename (replace dots with underscores to avoid path traversal)
        var sanitizedPackageId = packageId.Replace(".", "_", StringComparison.Ordinal);
        var filename = $"{sanitizedPackageId}_{version}.tgz";
        return Path.Combine(_cacheDirectory, filename);
    }

    /// <summary>
    /// Checks if a package is cached locally.
    /// </summary>
    /// <param name="packageId">Package ID</param>
    /// <param name="version">Package version</param>
    /// <returns>True if package exists in cache; otherwise false</returns>
    public bool IsCached(string packageId, string version)
    {
        var cachePath = GetCachePath(packageId, version);
        var exists = File.Exists(cachePath);

        if (exists)
        {
            var fileInfo = new FileInfo(cachePath);
            _logger.LogDebug(
                "Found cached package {PackageId}@{Version}. Size: {Size} bytes",
                packageId, version, fileInfo.Length);
        }

        return exists;
    }

    /// <summary>
    /// Reads a cached package from disk.
    /// </summary>
    /// <param name="packageId">Package ID</param>
    /// <param name="version">Package version</param>
    /// <returns>Stream containing the package data</returns>
    /// <exception cref="FileNotFoundException">Thrown when package is not cached</exception>
    public Stream ReadFromCache(string packageId, string version)
    {
        var cachePath = GetCachePath(packageId, version);

        if (!File.Exists(cachePath))
        {
            throw new FileNotFoundException(
                $"Package '{packageId}@{version}' not found in cache at '{cachePath}'");
        }

        _logger.LogInformation(
            "Reading cached package {PackageId}@{Version} from {CachePath}",
            packageId, version, cachePath);

        // Read entire file into memory stream
        var memoryStream = new MemoryStream();
        using (var fileStream = new FileStream(
            cachePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true))
        {
            fileStream.CopyTo(memoryStream);
        }

        memoryStream.Position = 0;
        return memoryStream;
    }

    /// <summary>
    /// Writes a downloaded package to the cache.
    /// </summary>
    /// <param name="packageId">Package ID</param>
    /// <param name="version">Package version</param>
    /// <param name="packageStream">Stream containing the package data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the asynchronous cache write operation</returns>
    public async Task WriteToCache(
        string packageId,
        string version,
        Stream packageStream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packageStream);

        var cachePath = GetCachePath(packageId, version);

        _logger.LogInformation(
            "Caching package {PackageId}@{Version} to {CachePath}",
            packageId, version, cachePath);

        try
        {
            // Reset stream position if possible
            if (packageStream.CanSeek)
            {
                packageStream.Position = 0;
            }

            // Write to cache file
            using (var fileStream = new FileStream(
                cachePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                await packageStream.CopyToAsync(fileStream, 81920, cancellationToken);
            }

            var fileInfo = new FileInfo(cachePath);
            _logger.LogInformation(
                "Successfully cached package {PackageId}@{Version}. Size: {Size} bytes",
                packageId, version, fileInfo.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to cache package {PackageId}@{Version} to {CachePath}",
                packageId, version, cachePath);

            // Try to clean up partial file
            try
            {
                if (File.Exists(cachePath))
                {
                    File.Delete(cachePath);
                }
            }
            catch (Exception deleteEx)
            {
                _logger.LogWarning(
                    deleteEx,
                    "Failed to clean up partial cache file for {PackageId}@{Version}",
                    packageId, version);
            }

            throw;
        }
    }

    /// <summary>
    /// Removes a package from the cache.
    /// </summary>
    /// <param name="packageId">Package ID</param>
    /// <param name="version">Package version</param>
    public void RemoveFromCache(string packageId, string version)
    {
        var cachePath = GetCachePath(packageId, version);

        if (!File.Exists(cachePath))
        {
            _logger.LogDebug(
                "Package {PackageId}@{Version} not in cache",
                packageId, version);
            return;
        }

        try
        {
            File.Delete(cachePath);
            _logger.LogInformation(
                "Removed cached package {PackageId}@{Version}",
                packageId, version);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to remove cached package {PackageId}@{Version} at {CachePath}",
                packageId, version, cachePath);
            throw;
        }
    }

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    /// <returns>Tuple of (total cached packages, total cache size in bytes)</returns>
    public (int PackageCount, long TotalSize) GetCacheStats()
    {
        try
        {
            var cacheDir = new DirectoryInfo(_cacheDirectory);
            if (!cacheDir.Exists)
            {
                return (0, 0);
            }

            var files = cacheDir.GetFiles("*.tgz");
            var totalSize = files.Sum(f => f.Length);

            _logger.LogDebug(
                "Cache statistics: {PackageCount} packages, {TotalSize} bytes",
                files.Length, totalSize);

            return (files.Length, totalSize);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to get cache statistics");
            return (0, 0);
        }
    }

    /// <summary>
    /// Clears all cached packages from disk.
    /// </summary>
    public void ClearCache()
    {
        try
        {
            var cacheDir = new DirectoryInfo(_cacheDirectory);
            if (!cacheDir.Exists)
            {
                return;
            }

            foreach (var file in cacheDir.GetFiles("*.tgz"))
            {
                file.Delete();
            }

            _logger.LogInformation("Cache cleared. Removed all cached packages.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear cache");
            throw;
        }
    }
}
