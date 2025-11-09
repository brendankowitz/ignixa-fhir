using Ignixa.PackageManagement.Models;

namespace Ignixa.PackageManagement.Abstractions;

/// <summary>
/// High-level orchestration interface for package management.
/// </summary>
public interface IImplementationGuideProvider
{
    /// <summary>
    /// Loads a package from the NPM registry and imports to database.
    /// </summary>
    /// <param name="packageId">Package ID (e.g., "hl7.fhir.us.core")</param>
    /// <param name="version">Package version (e.g., "5.0.1")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Import result with statistics</returns>
    Task<PackageImportResult> LoadPackageAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists all currently loaded packages.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of (packageId, version) tuples</returns>
    Task<IReadOnlyList<(string PackageId, string Version)>> ListLoadedPackagesAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Unloads (deactivates) a package, making its resources unavailable.
    /// </summary>
    /// <param name="packageId">Package ID</param>
    /// <param name="version">Package version</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of resources deactivated</returns>
    Task<int> UnloadPackageAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken);
}
