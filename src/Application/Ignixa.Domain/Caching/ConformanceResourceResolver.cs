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
public partial class ConformanceResourceResolver : IConformanceResourceResolver
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
            LogResolvedFromCache(_logger, canonical, version ?? "latest");
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
            LogResolvedFromDatabase(_logger, canonical, packageResource.Version, packageResource.PackageId, packageResource.PackageVersion);

            // Cache for future requests
            await _cache.SetAsync(tenantId, canonical, packageResource.ResourceJson, cancellationToken: cancellationToken);

            return packageResource.ResourceJson;
        }

        // Step 3: Not found
        LogResourceNotFound(_logger, canonical, version ?? "latest");

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

        LogResolvingFromPackage(_logger, packageId, packageVersion, canonical);

        var packageResource = await _packageRepository.GetFromPackageAsync(
            packageId, packageVersion, canonical, cancellationToken);

        if (packageResource != null && packageResource.IsActive)
        {
            LogResolvedFromPackage(_logger, packageId, packageVersion);

            return packageResource.ResourceJson;
        }

        LogResourceNotFoundInPackage(_logger, packageId, packageVersion);

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

        LogListingResources(_logger, packageId, packageVersion, resourceType ?? "all");

        var resources = await _packageRepository.ListPackageResourcesAsync(
            packageId, packageVersion, resourceType, cancellationToken);

        LogFoundResources(_logger, resources.Count, packageId, packageVersion);

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

        LogInvalidatingTenantCache(_logger, tenantId);

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

        LogInvalidatingResourceCache(_logger, canonical);

        await _cache.InvalidateAsync(tenantId, canonical, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Conformance resource resolved from cache. Canonical: {Canonical}, Version: {Version}")]
    private static partial void LogResolvedFromCache(ILogger logger, string canonical, string version);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Conformance resource resolved from database. Canonical: {Canonical}, Version: {Version}, Package: {PackageId}@{PackageVersion}")]
    private static partial void LogResolvedFromDatabase(ILogger logger, string canonical, string? version, string packageId, string packageVersion);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Conformance resource not found. Canonical: {Canonical}, Version: {Version}")]
    private static partial void LogResourceNotFound(ILogger logger, string canonical, string version);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Resolving conformance resource from package. Package: {PackageId}@{PackageVersion}, Canonical: {Canonical}")]
    private static partial void LogResolvingFromPackage(ILogger logger, string packageId, string packageVersion, string canonical);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Conformance resource resolved from package {PackageId}@{PackageVersion}")]
    private static partial void LogResolvedFromPackage(ILogger logger, string packageId, string packageVersion);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Conformance resource not found in package {PackageId}@{PackageVersion}")]
    private static partial void LogResourceNotFoundInPackage(ILogger logger, string packageId, string packageVersion);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Listing conformance resources from package {PackageId}@{PackageVersion}. Filter: {ResourceType}")]
    private static partial void LogListingResources(ILogger logger, string packageId, string packageVersion, string resourceType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {Count} conformance resources in package {PackageId}@{PackageVersion}")]
    private static partial void LogFoundResources(ILogger logger, int count, string packageId, string packageVersion);

    [LoggerMessage(Level = LogLevel.Information, Message = "Invalidating conformance cache for tenant {TenantId}")]
    private static partial void LogInvalidatingTenantCache(ILogger logger, string tenantId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Invalidating conformance resource cache. Canonical: {Canonical}")]
    private static partial void LogInvalidatingResourceCache(ILogger logger, string canonical);
}
