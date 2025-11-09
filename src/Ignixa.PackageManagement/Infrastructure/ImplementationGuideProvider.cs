using Ignixa.Domain.Abstractions;
using Ignixa.PackageManagement.Abstractions;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.PackageManagement.Infrastructure;

/// <summary>
/// High-level orchestration for package management.
/// Coordinates downloading, extracting, and importing FHIR packages.
/// </summary>
public class ImplementationGuideProvider : IImplementationGuideProvider
{
    private readonly IPackageLoader _packageLoader;
    private readonly IPackageExtractor _packageExtractor;
    private readonly IPackageResourceImporter _packageImporter;
    private readonly IPackageResourceRepository _packageRepository;
    private readonly ILogger<ImplementationGuideProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the ImplementationGuideProvider class.
    /// </summary>
    /// <param name="packageLoader">Loader for downloading packages</param>
    /// <param name="packageExtractor">Extractor for parsing packages</param>
    /// <param name="packageImporter">Importer for storing resources</param>
    /// <param name="packageRepository">Repository for package resources</param>
    /// <param name="logger">Logger instance</param>
    public ImplementationGuideProvider(
        IPackageLoader packageLoader,
        IPackageExtractor packageExtractor,
        IPackageResourceImporter packageImporter,
        IPackageResourceRepository packageRepository,
        ILogger<ImplementationGuideProvider> logger)
    {
        _packageLoader = packageLoader ?? throw new ArgumentNullException(nameof(packageLoader));
        _packageExtractor = packageExtractor ?? throw new ArgumentNullException(nameof(packageExtractor));
        _packageImporter = packageImporter ?? throw new ArgumentNullException(nameof(packageImporter));
        _packageRepository = packageRepository ?? throw new ArgumentNullException(nameof(packageRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Loads a package from the NPM registry and imports to database.
    /// </summary>
    /// <param name="packageId">Package ID (e.g., "hl7.fhir.us.core")</param>
    /// <param name="version">Package version (e.g., "5.0.1")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Import result with statistics</returns>
    public async Task<PackageImportResult> LoadPackageAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("Package ID cannot be null or empty", nameof(packageId));
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version cannot be null or empty", nameof(version));

        _logger.LogInformation(
            "Loading package {PackageId}@{Version}",
            packageId, version);

        try
        {
            // Step 1: Download package
            _logger.LogDebug("Step 1/3: Downloading package {PackageId}@{Version}", packageId, version);
            using var packageStream = await _packageLoader.DownloadPackageAsync(
                packageId, version, cancellationToken);

            // Step 2: Extract resources
            _logger.LogDebug("Step 2/3: Extracting resources from package {PackageId}@{Version}", packageId, version);
            var extraction = await _packageExtractor.ExtractAsync(packageStream, cancellationToken);

            // Step 3: Import to database
            _logger.LogDebug("Step 3/3: Importing resources to database for {PackageId}@{Version}", packageId, version);
            var result = await _packageImporter.ImportAsync(extraction, cancellationToken);

            _logger.LogInformation(
                "Package {PackageId}@{Version} loaded successfully. Imported {Count} resources in {Duration}ms",
                packageId, version, result.ImportedResources, result.Duration.TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to load package {PackageId}@{Version}",
                packageId, version);
            throw;
        }
    }

    /// <summary>
    /// Lists all currently loaded packages.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of (packageId, version) tuples</returns>
    public async Task<IReadOnlyList<(string PackageId, string Version)>> ListLoadedPackagesAsync(
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing loaded packages");

        try
        {
            var packages = await _packageRepository.ListLoadedPackagesAsync(cancellationToken);

            _logger.LogInformation("Found {Count} loaded packages", packages.Count);

            return packages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list loaded packages");
            throw;
        }
    }

    /// <summary>
    /// Unloads (deactivates) a package, making its resources unavailable.
    /// </summary>
    /// <param name="packageId">Package ID</param>
    /// <param name="version">Package version</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of resources deactivated</returns>
    public async Task<int> UnloadPackageAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("Package ID cannot be null or empty", nameof(packageId));
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version cannot be null or empty", nameof(version));

        _logger.LogInformation(
            "Unloading package {PackageId}@{Version}",
            packageId, version);

        try
        {
            var count = await _packageRepository.DeactivatePackageAsync(
                packageId, version, cancellationToken);

            _logger.LogInformation(
                "Package {PackageId}@{Version} unloaded. Deactivated {Count} resources",
                packageId, version, count);

            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to unload package {PackageId}@{Version}",
                packageId, version);
            throw;
        }
    }
}
