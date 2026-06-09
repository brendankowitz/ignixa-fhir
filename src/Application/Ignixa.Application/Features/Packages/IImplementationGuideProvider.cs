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
    /// Loads a package and its full declared dependency closure into a tenant's database.
    /// Walks <see cref="PackageManifest.Dependencies"/> breadth-first, deduplicating by
    /// package id, skipping <c>hl7.fhir.r4.core</c> (provided in-process by the base
    /// schema provider, not distributed as a downloadable tarball), and tolerating
    /// individual dependency failures so a missing transitive dep does not abort the
    /// whole operation.
    /// </summary>
    /// <param name="tenantId">Tenant ID for database selection.</param>
    /// <param name="packageId">Root package id.</param>
    /// <param name="version">Root package version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Aggregated import result. <see cref="PackageImportResult.PackageId"/> /
    /// <see cref="PackageImportResult.PackageVersion"/> refer to the root package;
    /// <see cref="PackageImportResult.ImportedResources"/> /
    /// <see cref="PackageImportResult.ResourcesByType"/> sum across the closure;
    /// <see cref="PackageImportResult.LoadedPackages"/> lists every package visited
    /// (newly imported or already-loaded).
    /// </returns>
    Task<PackageImportResult> LoadPackageWithDependenciesAsync(
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
