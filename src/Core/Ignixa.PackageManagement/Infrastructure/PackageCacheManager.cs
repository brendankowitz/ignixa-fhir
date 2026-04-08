using Microsoft.Extensions.Logging;

namespace Ignixa.PackageManagement.Infrastructure;

/// <summary>
/// Manages local filesystem caching of downloaded FHIR packages.
/// Prevents re-downloading packages and supports offline access.
/// </summary>
public partial class PackageCacheManager
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
            LogFoundCachedPackage(_logger, packageId, version, fileInfo.Length);
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

        LogReadingCachedPackage(_logger, packageId, version, cachePath);

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

        LogCachingPackage(_logger, packageId, version, cachePath);

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
            LogSuccessfullyCachedPackage(_logger, packageId, version, fileInfo.Length);
        }
        catch (Exception ex)
        {
            LogCacheFailed(_logger, ex, packageId, version, cachePath);

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
                LogCleanupFailed(_logger, deleteEx, packageId, version);
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
            LogPackageNotInCache(_logger, packageId, version);
            return;
        }

        try
        {
            File.Delete(cachePath);
            LogRemovedCachedPackage(_logger, packageId, version);
        }
        catch (Exception ex)
        {
            LogRemoveFailed(_logger, ex, packageId, version, cachePath);
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

            LogCacheStats(_logger, files.Length, totalSize);

            return (files.Length, totalSize);
        }
        catch (Exception ex)
        {
            LogCacheStatsFailed(_logger, ex);
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

            LogCacheCleared(_logger);
        }
        catch (Exception ex)
        {
            LogClearCacheFailed(_logger, ex);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found cached package {PackageId}@{Version}. Size: {Size} bytes")]
    private static partial void LogFoundCachedPackage(ILogger logger, string packageId, string version, long size);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reading cached package {PackageId}@{Version} from {CachePath}")]
    private static partial void LogReadingCachedPackage(ILogger logger, string packageId, string version, string cachePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Caching package {PackageId}@{Version} to {CachePath}")]
    private static partial void LogCachingPackage(ILogger logger, string packageId, string version, string cachePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully cached package {PackageId}@{Version}. Size: {Size} bytes")]
    private static partial void LogSuccessfullyCachedPackage(ILogger logger, string packageId, string version, long size);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to cache package {PackageId}@{Version} to {CachePath}")]
    private static partial void LogCacheFailed(ILogger logger, Exception ex, string packageId, string version, string cachePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to clean up partial cache file for {PackageId}@{Version}")]
    private static partial void LogCleanupFailed(ILogger logger, Exception ex, string packageId, string version);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Package {PackageId}@{Version} not in cache")]
    private static partial void LogPackageNotInCache(ILogger logger, string packageId, string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed cached package {PackageId}@{Version}")]
    private static partial void LogRemovedCachedPackage(ILogger logger, string packageId, string version);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to remove cached package {PackageId}@{Version} at {CachePath}")]
    private static partial void LogRemoveFailed(ILogger logger, Exception ex, string packageId, string version, string cachePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache statistics: {PackageCount} packages, {TotalSize} bytes")]
    private static partial void LogCacheStats(ILogger logger, int packageCount, long totalSize);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to get cache statistics")]
    private static partial void LogCacheStatsFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cache cleared. Removed all cached packages.")]
    private static partial void LogCacheCleared(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to clear cache")]
    private static partial void LogClearCacheFailed(ILogger logger, Exception ex);
}
