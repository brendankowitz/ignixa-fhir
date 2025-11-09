using Ignixa.PackageManagement.Models;

namespace Ignixa.PackageManagement.Abstractions;

/// <summary>
/// High-level orchestration interface for package management.
/// </summary>
public interface IImplementationGuideProvider
{
    /// <summary>
    /// Loads a package from the NPM registry and imports to a tenant's database.
    /// </summary>
    /// <param name="tenantId">Tenant ID for database selection</param>
    /// <param name="packageId">Package ID (e.g., "hl7.fhir.us.core")</param>
    /// <param name="version">Package version (e.g., "5.0.1")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Import result with statistics</returns>
    Task<PackageImportResult> LoadPackageAsync(
        string tenantId,
        string packageId,
        string version,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists all currently loaded packages for a specific tenant.
    /// </summary>
    /// <param name="tenantId">Tenant ID for database selection</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of (packageId, version) tuples</returns>
    Task<IReadOnlyList<(string PackageId, string Version)>> ListLoadedPackagesAsync(
        string tenantId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Unloads (deactivates) a package from a tenant's database, making its resources unavailable.
    /// </summary>
    /// <param name="tenantId">Tenant ID for database selection</param>
    /// <param name="packageId">Package ID</param>
    /// <param name="version">Package version</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of resources deactivated</returns>
    Task<int> UnloadPackageAsync(
        string tenantId,
        string packageId,
        string version,
        CancellationToken cancellationToken);
}
