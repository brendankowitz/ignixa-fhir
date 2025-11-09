using Ignixa.PackageManagement.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.PackageManagement.Infrastructure;

/// <summary>
/// Downloads FHIR NPM packages from the packages.fhir.org registry.
/// </summary>
public class NpmPackageLoader : IPackageLoader
{
    private const string DefaultRegistryUrl = "https://packages.fhir.org";
    private readonly HttpClient _httpClient;
    private readonly ILogger<NpmPackageLoader> _logger;

    /// <summary>
    /// Initializes a new instance of the NpmPackageLoader class.
    /// </summary>
    /// <param name="httpClient">HTTP client for downloading packages</param>
    /// <param name="logger">Logger instance</param>
    public NpmPackageLoader(HttpClient httpClient, ILogger<NpmPackageLoader> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Downloads a FHIR package from the NPM registry.
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
    /// Builds the full URL for a package download.
    /// </summary>
    private static string BuildPackageUrl(string packageId, string version)
    {
        // Standard NPM registry URL format: https://packages.fhir.org/{packageId}/{version}
        return $"{DefaultRegistryUrl}/{packageId}/{version}";
    }
}
