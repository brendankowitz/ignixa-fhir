using Microsoft.Extensions.Logging;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;

namespace Ignixa.Domain.Caching;

/// <summary>
/// Resolves FHIR conformance resources using a fallback chain.
/// Chain: Cache → PackageResource → Resource → NotFound
/// NOTE: Currently uses a global IPackageResourceRepository for all tenants.
/// Phase 2: Will be updated to use tenant-scoped repositories when IFhirRepository.PackageResources is available.
/// </summary>
public class ConformanceResourceResolver : IConformanceResourceResolver
{
    private readonly IFhirConformanceCache _cache;
    private readonly IPackageResourceRepository _packageRepository;
    private readonly ILogger<ConformanceResourceResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the ConformanceResourceResolver class.
    /// </summary>
    /// <param name="cache">Conformance cache</param>
    /// <param name="packageRepository">Package resource repository</param>
    /// <param name="logger">Logger instance</param>
    public ConformanceResourceResolver(
        IFhirConformanceCache cache,
        IPackageResourceRepository packageRepository,
        ILogger<ConformanceResourceResolver> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _packageRepository = packageRepository ?? throw new ArgumentNullException(nameof(packageRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Resolves a conformance resource by canonical URL.
    /// Uses fallback chain: Cache → PackageResource → NotFound
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="canonical">Canonical URL</param>
    /// <param name="version">Resource version (optional, uses latest if not specified)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Resource JSON string, or null if not found</returns>
    public async Task<string?> ResolveAsync(
        string tenantId,
        string canonical,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Tenant ID cannot be null or empty", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(canonical))
            throw new ArgumentException("Canonical URL cannot be null or empty", nameof(canonical));

        // Step 1: Try cache
        var cached = await _cache.GetAsync(tenantId, canonical, version, cancellationToken);
        if (cached != null)
        {
            _logger.LogDebug(
                "Conformance resource resolved from cache. Canonical: {Canonical}, Version: {Version}",
                canonical, version ?? "latest");
            return cached;
        }

        // Step 2: Try database (package resources)
        PackageResource? packageResource = null;

        if (version != null)
        {
            // Exact version lookup
            packageResource = await _packageRepository.GetByCanonicalAsync(
                canonical, version, cancellationToken);
        }
        else
        {
            // Latest version lookup
            packageResource = await _packageRepository.GetLatestByCanonicalAsync(
                canonical, resourceType: null, cancellationToken);
        }

        if (packageResource != null && packageResource.IsActive)
        {
            _logger.LogDebug(
                "Conformance resource resolved from database. Canonical: {Canonical}, Version: {Version}, Package: {PackageId}@{PackageVersion}",
                canonical, packageResource.Version, packageResource.PackageId, packageResource.PackageVersion);

            // Cache for future requests
            await _cache.SetAsync(tenantId, canonical, packageResource.ResourceJson, cancellationToken: cancellationToken);

            return packageResource.ResourceJson;
        }

        // Step 3: Not found
        _logger.LogDebug(
            "Conformance resource not found. Canonical: {Canonical}, Version: {Version}",
            canonical, version ?? "latest");

        return null;
    }

    /// <summary>
    /// Resolves a conformance resource from a specific package version.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="packageId">Package ID</param>
    /// <param name="packageVersion">Package version</param>
    /// <param name="canonical">Canonical URL</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Resource JSON string, or null if not found</returns>
    public async Task<string?> ResolveFromPackageAsync(
        string tenantId,
        string packageId,
        string packageVersion,
        string canonical,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Tenant ID cannot be null or empty", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("Package ID cannot be null or empty", nameof(packageId));
        if (string.IsNullOrWhiteSpace(packageVersion))
            throw new ArgumentException("Package version cannot be null or empty", nameof(packageVersion));
        if (string.IsNullOrWhiteSpace(canonical))
            throw new ArgumentException("Canonical URL cannot be null or empty", nameof(canonical));

        _logger.LogDebug(
            "Resolving conformance resource from package. Package: {PackageId}@{PackageVersion}, Canonical: {Canonical}",
            packageId, packageVersion, canonical);

        var packageResource = await _packageRepository.GetFromPackageAsync(
            packageId, packageVersion, canonical, cancellationToken);

        if (packageResource != null && packageResource.IsActive)
        {
            _logger.LogDebug(
                "Conformance resource resolved from package {PackageId}@{PackageVersion}",
                packageId, packageVersion);

            return packageResource.ResourceJson;
        }

        _logger.LogDebug(
            "Conformance resource not found in package {PackageId}@{PackageVersion}",
            packageId, packageVersion);

        return null;
    }

    /// <summary>
    /// Lists all available conformance resources from a package.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="packageId">Package ID</param>
    /// <param name="packageVersion">Package version</param>
    /// <param name="resourceType">Optional: Filter by resource type</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of available resources</returns>
    public async Task<IReadOnlyList<PackageResource>> ListPackageResourcesAsync(
        string tenantId,
        string packageId,
        string packageVersion,
        string? resourceType = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Tenant ID cannot be null or empty", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("Package ID cannot be null or empty", nameof(packageId));
        if (string.IsNullOrWhiteSpace(packageVersion))
            throw new ArgumentException("Package version cannot be null or empty", nameof(packageVersion));

        _logger.LogDebug(
            "Listing conformance resources from package {PackageId}@{PackageVersion}. Filter: {ResourceType}",
            packageId, packageVersion, resourceType ?? "all");

        var resources = await _packageRepository.ListPackageResourcesAsync(
            packageId, packageVersion, resourceType, cancellationToken);

        _logger.LogDebug(
            "Found {Count} conformance resources in package {PackageId}@{PackageVersion}",
            resources.Count, packageId, packageVersion);

        return resources;
    }

    /// <summary>
    /// Invalidates cached resources for a tenant.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task InvalidateTenantCacheAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Tenant ID cannot be null or empty", nameof(tenantId));

        _logger.LogInformation("Invalidating conformance cache for tenant {TenantId}", tenantId);

        await _cache.InvalidateTenantAsync(tenantId, cancellationToken);
    }

    /// <summary>
    /// Invalidates a specific cached resource.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="canonical">Canonical URL</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task InvalidateResourceAsync(
        string tenantId,
        string canonical,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Tenant ID cannot be null or empty", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(canonical))
            throw new ArgumentException("Canonical URL cannot be null or empty", nameof(canonical));

        _logger.LogDebug(
            "Invalidating conformance resource cache. Canonical: {Canonical}",
            canonical);

        await _cache.InvalidateAsync(tenantId, canonical, cancellationToken);
    }
}
