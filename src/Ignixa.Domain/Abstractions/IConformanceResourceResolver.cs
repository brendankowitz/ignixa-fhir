using Ignixa.Domain.Models;

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Resolves FHIR conformance resources using a fallback chain.
/// Integrates caching, package resources, and database lookups.
/// </summary>
public interface IConformanceResourceResolver
{
    /// <summary>
    /// Resolves a conformance resource by canonical URL.
    /// Uses fallback chain: Cache → PackageResource → NotFound
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="canonical">Canonical URL</param>
    /// <param name="version">Resource version (optional, uses latest if not specified)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Resource JSON string, or null if not found</returns>
    Task<string?> ResolveAsync(
        string tenantId,
        string canonical,
        string? version = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a conformance resource from a specific package version.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="packageId">Package ID</param>
    /// <param name="packageVersion">Package version</param>
    /// <param name="canonical">Canonical URL</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Resource JSON string, or null if not found</returns>
    Task<string?> ResolveFromPackageAsync(
        string tenantId,
        string packageId,
        string packageVersion,
        string canonical,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all available conformance resources from a package.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="packageId">Package ID</param>
    /// <param name="packageVersion">Package version</param>
    /// <param name="resourceType">Optional: Filter by resource type</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of available resources</returns>
    Task<IReadOnlyList<PackageResource>> ListPackageResourcesAsync(
        string tenantId,
        string packageId,
        string packageVersion,
        string? resourceType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates cached resources for a tenant.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task InvalidateTenantCacheAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates a specific cached resource.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="canonical">Canonical URL</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task InvalidateResourceAsync(
        string tenantId,
        string canonical,
        CancellationToken cancellationToken = default);
}
