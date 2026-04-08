using Ignixa.Abstractions;
using Ignixa.PackageManagement.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.PackageManagement.Infrastructure;

/// <summary>
/// Downloads FHIR NPM packages from a configurable NPM registry.
/// Supports local caching to prevent re-downloading packages.
/// Default registry: https://packages.fhir.org
/// </summary>
public partial class NpmPackageLoader : IPackageLoader
{
    private readonly HttpClient _httpClient;
    private readonly PackageCacheManager? _cacheManager;
    private readonly NpmPackageLoaderOptions _options;
    private readonly ILogger<NpmPackageLoader> _logger;

    /// <summary>
    /// Initializes a new instance of the NpmPackageLoader class without caching.
    /// Uses default options (https://packages.fhir.org).
    /// </summary>
    /// <param name="httpClient">HTTP client for downloading packages</param>
    /// <param name="logger">Logger instance</param>
    public NpmPackageLoader(HttpClient httpClient, ILogger<NpmPackageLoader> logger)
        : this(httpClient, cacheManager: null, options: null, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the NpmPackageLoader class with optional caching and custom options.
    /// </summary>
    /// <param name="httpClient">HTTP client for downloading packages</param>
    /// <param name="cacheManager">Optional cache manager for local package caching</param>
    /// <param name="options">Optional configuration options (registry URL, timeouts, etc.). If null, uses defaults.</param>
    /// <param name="logger">Logger instance</param>
    public NpmPackageLoader(
        HttpClient httpClient,
        PackageCacheManager? cacheManager,
        NpmPackageLoaderOptions? options,
        ILogger<NpmPackageLoader> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cacheManager = cacheManager;
        _options = options ?? new NpmPackageLoaderOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Log the configured registry URL
        LogInitialized(_logger, _options.RegistryUrl);
    }

    /// <summary>
    /// Downloads a FHIR package from the NPM registry.
    /// Checks local cache first before downloading from registry.
    /// </summary>
    /// <param name="packageId">Package ID (e.g., "hl7.fhir.us.core")</param>
    /// <param name="version">Package version (e.g., "5.0.1")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stream containing the package .tgz file</returns>
    /// <exception cref="ArgumentException">Thrown when packageId or version is null or empty</exception>
    /// <exception cref="HttpRequestException">Thrown when download fails</exception>
    public async Task<Stream> DownloadPackageAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("Package ID cannot be null or empty", nameof(packageId));
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version cannot be null or empty", nameof(version));

        // Step 1: Check local cache
        if (_cacheManager != null && _cacheManager.IsCached(packageId, version))
        {
            LogFoundCachedPackage(_logger, packageId, version);
            return _cacheManager.ReadFromCache(packageId, version);
        }

        var url = BuildPackageUrl(packageId, version);

        LogDownloadingPackage(_logger, packageId, version, url);

        try
        {
            var uri = new Uri(url);
            var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            LogDownloadStarted(_logger, packageId, version, response.Content.Headers.ContentLength);

            // Read entire response to memory stream
            var memoryStream = new MemoryStream();
            await response.Content.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;

            LogDownloadComplete(_logger, packageId, version, memoryStream.Length);

            // Step 2: Cache the downloaded package
            if (_cacheManager != null)
            {
                try
                {
                    await _cacheManager.WriteToCache(packageId, version, memoryStream, cancellationToken);
                    memoryStream.Position = 0; // Reset position after caching
                }
                catch (Exception cacheEx)
                {
                    LogCacheWriteFailed(_logger, cacheEx, packageId, version);
                    // Don't fail the operation if caching fails
                    memoryStream.Position = 0;
                }
            }

            return memoryStream;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            LogPackageNotFoundInRegistry(_logger, ex, packageId, version);
            throw new InvalidOperationException(
                $"Package '{packageId}@{version}' not found in NPM registry", ex);
        }
        catch (HttpRequestException ex)
        {
            LogDownloadFailed(_logger, ex, packageId, version, ex.StatusCode);
            throw;
        }
        catch (Exception ex)
        {
            LogUnexpectedDownloadError(_logger, ex, packageId, version);
            throw;
        }
    }

    /// <summary>
    /// Builds the full URL for a package download using the configured registry URL.
    /// </summary>
    private string BuildPackageUrl(string packageId, string version)
    {
        // Standard NPM registry URL format: {registryUrl}/{packageId}/{version}
        // Example: https://packages.fhir.org/hl7.fhir.us.core/5.0.1
        return $"{_options.RegistryUrl.TrimEnd('/')}/{packageId}/{version}";
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "NpmPackageLoader initialized with registry URL: {RegistryUrl}")]
    private static partial void LogInitialized(ILogger logger, string registryUrl);

    [LoggerMessage(Level = LogLevel.Information, Message = "Found cached package {PackageId}@{Version}")]
    private static partial void LogFoundCachedPackage(ILogger logger, string packageId, string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloading FHIR package {PackageId}@{Version} from {Url}")]
    private static partial void LogDownloadingPackage(ILogger logger, string packageId, string version, string url);

    [LoggerMessage(Level = LogLevel.Information, Message = "Package {PackageId}@{Version} download started. Size: {ContentLength} bytes")]
    private static partial void LogDownloadStarted(ILogger logger, string packageId, string version, long? contentLength);

    [LoggerMessage(Level = LogLevel.Information, Message = "Package {PackageId}@{Version} downloaded successfully. Total size: {Size} bytes")]
    private static partial void LogDownloadComplete(ILogger logger, string packageId, string version, long size);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to cache package {PackageId}@{Version}, but download was successful. Proceeding without cache.")]
    private static partial void LogCacheWriteFailed(ILogger logger, Exception ex, string packageId, string version);

    [LoggerMessage(Level = LogLevel.Error, Message = "Package {PackageId}@{Version} not found in registry")]
    private static partial void LogPackageNotFoundInRegistry(ILogger logger, Exception ex, string packageId, string version);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to download package {PackageId}@{Version}. Status: {StatusCode}")]
    private static partial void LogDownloadFailed(ILogger logger, Exception ex, string packageId, string version, System.Net.HttpStatusCode? statusCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected error downloading package {PackageId}@{Version}")]
    private static partial void LogUnexpectedDownloadError(ILogger logger, Exception ex, string packageId, string version);
}
