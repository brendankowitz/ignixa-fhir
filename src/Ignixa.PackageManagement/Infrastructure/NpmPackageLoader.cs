using Ignixa.Abstractions;
using Ignixa.PackageManagement.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.PackageManagement.Infrastructure;

/// <summary>
/// Downloads FHIR NPM packages from a configurable NPM registry.
/// Supports local caching to prevent re-downloading packages.
/// Default registry: https://packages.fhir.org
/// </summary>
public class NpmPackageLoader : IPackageLoader
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
        _logger.LogDebug("NpmPackageLoader initialized with registry URL: {RegistryUrl}", _options.RegistryUrl);
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
            _logger.LogInformation(
                "Found cached package {PackageId}@{Version}",
                packageId, version);
            return _cacheManager.ReadFromCache(packageId, version);
        }

        var url = BuildPackageUrl(packageId, version);

        _logger.LogInformation(
            "Downloading FHIR package {PackageId}@{Version} from {Url}",
            packageId, version, url);

        try
        {
            var uri = new Uri(url);
            var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation(
                "Package {PackageId}@{Version} download started. Size: {ContentLength} bytes",
                packageId, version, response.Content.Headers.ContentLength);

            // Read entire response to memory stream
            var memoryStream = new MemoryStream();
            await response.Content.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;

            _logger.LogInformation(
                "Package {PackageId}@{Version} downloaded successfully. Total size: {Size} bytes",
                packageId, version, memoryStream.Length);

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
                    _logger.LogWarning(
                        cacheEx,
                        "Failed to cache package {PackageId}@{Version}, but download was successful. Proceeding without cache.",
                        packageId, version);
                    // Don't fail the operation if caching fails
                    memoryStream.Position = 0;
                }
            }

            return memoryStream;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogError(
                ex,
                "Package {PackageId}@{Version} not found in registry",
                packageId, version);
            throw new InvalidOperationException(
                $"Package '{packageId}@{version}' not found in NPM registry", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "Failed to download package {PackageId}@{Version}. Status: {StatusCode}",
                packageId, version, ex.StatusCode);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error downloading package {PackageId}@{Version}",
                packageId, version);
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
}
