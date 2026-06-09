using Ignixa.Domain.Abstractions;
using Ignixa.PackageManagement.Abstractions;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.PackageManagement.Infrastructure;

/// <summary>
/// High-level orchestration for package management.
/// Coordinates downloading, extracting, and importing FHIR packages.
/// Supports multi-tenant deployments by obtaining tenant-specific repositories.
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
    /// <param name="packageRepository">Package resource repository</param>
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
    /// Loads a package from the NPM registry and imports to a tenant's database.
    /// </summary>
    public async Task<PackageImportResult> LoadPackageAsync(
        string tenantId,
        string packageId,
        string version,
        CancellationToken cancellationToken)
    {
        var (result, _) = await LoadPackageInternalAsync(tenantId, packageId, version, cancellationToken);
        return result;
    }

    /// <summary>
    /// Internal load path that also returns the extracted <see cref="PackageManifest"/>.
    /// Callers that need the manifest (e.g. transitive dep walkers) avoid a second
    /// download/extract round-trip; <see cref="LoadPackageAsync"/> discards it.
    /// Returns a null manifest when the package was already loaded (idempotent
    /// short-circuit) - callers needing the manifest in that case must re-fetch.
    /// </summary>
    private async Task<(PackageImportResult Result, PackageManifest? Manifest)> LoadPackageInternalAsync(
        string tenantId,
        string packageId,
        string version,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Tenant ID cannot be null or empty", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("Package ID cannot be null or empty", nameof(packageId));
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version cannot be null or empty", nameof(version));

        _logger.LogInformation(
            "Loading package {PackageId}@{Version} into tenant {TenantId}",
            packageId, version, tenantId);

        try
        {
            // Step 0: Check if package already loaded (idempotent operation)
            var tenantIdInt = int.Parse(tenantId);
            var alreadyLoaded = await _packageRepository.PackageVersionExistsAsync(
                packageId, version, tenantIdInt, cancellationToken);

            if (alreadyLoaded)
            {
                _logger.LogInformation(
                    "Package {PackageId}@{Version} already loaded for tenant {TenantId}, skipping import (idempotent)",
                    packageId, version, tenantId);

                var emptyResult = new PackageImportResult
                {
                    PackageId = packageId,
                    PackageVersion = version,
                    TotalResources = 0,
                    ImportedResources = 0,
                    Duration = TimeSpan.Zero,
                    ResourcesByType = new Dictionary<string, int>()
                };
                return (emptyResult, Manifest: null);
            }

            // Step 1: Download package
            _logger.LogDebug("Step 1/3: Downloading package {PackageId}@{Version}", packageId, version);
            using var packageStream = await _packageLoader.DownloadPackageAsync(
                packageId, version, cancellationToken);

            // Step 2: Extract resources
            _logger.LogDebug("Step 2/3: Extracting resources from package {PackageId}@{Version}", packageId, version);
            var extraction = await _packageExtractor.ExtractAsync(packageStream, cancellationToken);

            // Step 3: Import to database
            _logger.LogDebug("Step 3/3: Importing resources to database for {PackageId}@{Version}", packageId, version);
            var result = await _packageImporter.ImportAsync(extraction, _packageRepository, cancellationToken);

            _logger.LogInformation(
                "Package {PackageId}@{Version} loaded successfully into tenant {TenantId}. Imported {Count} resources in {Duration}ms",
                packageId, version, tenantId, result.ImportedResources, result.Duration.TotalMilliseconds);

            return (result, extraction.Manifest);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to load package {PackageId}@{Version} into tenant {TenantId}",
                packageId, version, tenantId);
            throw;
        }
    }

    /// <summary>
    /// Loads a package and its full declared dependency closure into a tenant's database.
    /// Re-uses the existing single-package download/extract/import pipeline for each
    /// member of the closure so per-package idempotency, logging, and repository
    /// semantics are preserved.
    /// </summary>
    public async Task<PackageImportResult> LoadPackageWithDependenciesAsync(
        string tenantId,
        string packageId,
        string version,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Tenant ID cannot be null or empty", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("Package ID cannot be null or empty", nameof(packageId));
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version cannot be null or empty", nameof(version));

        _logger.LogInformation(
            "Loading package {PackageId}@{Version} with dependencies into tenant {TenantId}",
            packageId, version, tenantId);

        // BFS the closure. Skip r4.core (in-process) and dedup by package id so a diamond
        // dependency graph doesn't double-load anything.
        var visitedVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["hl7.fhir.r4.core"] = "*"
        };
        var queue = new Queue<(string Id, string Version)>();
        queue.Enqueue((packageId, version));

        var aggregatedResourcesByType = new Dictionary<string, int>(StringComparer.Ordinal);
        var loadedSpecs = new List<string>();
        var skippedSpecs = new List<string>();
        int aggregatedTotal = 0;
        int aggregatedImported = 0;
        int aggregatedUpdated = 0;
        var aggregatedDuration = TimeSpan.Zero;

        while (queue.Count > 0)
        {
            var (id, ver) = queue.Dequeue();
            if (visitedVersions.TryGetValue(id, out var existingVer))
            {
                if (existingVer != "*" && !string.Equals(existingVer, ver, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "Version conflict for {PackageId}: already loading @{ExistingVersion}, skipping @{RequestedVersion}",
                        id, existingVer, ver);
                    skippedSpecs.Add($"{id}@{ver} (version conflict: @{existingVer} already queued)");
                }
                continue;
            }
            visitedVersions[id] = ver;

            PackageImportResult perPackageResult;
            PackageManifest? manifest;
            try
            {
                (perPackageResult, manifest) = await LoadPackageInternalAsync(tenantId, id, ver, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to load transitive dependency {PackageId}@{Version} of {RootPackageId}@{RootVersion} - skipping",
                    id, ver, packageId, version);
                skippedSpecs.Add($"{id}@{ver} (skipped: {ex.GetType().Name}: {ex.Message})");
                continue;
            }

            // Was the import a no-op (already loaded)? If so we don't have a manifest in hand
            // but we still need to walk that package's dependencies. Best-effort re-fetch via
            // PackageCacheManager - the local tarball is typically already on disk.
            if (manifest == null)
            {
                try
                {
                    using var stream = await _packageLoader.DownloadPackageAsync(id, ver, cancellationToken);
                    var refetched = await _packageExtractor.ExtractAsync(stream, cancellationToken);
                    manifest = refetched.Manifest;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to re-read manifest of already-loaded package {PackageId}@{Version} - skipping its transitive deps",
                        id, ver);
                }
            }

            if (manifest?.Dependencies is { Count: > 0 } deps)
            {
                foreach (var dep in deps)
                {
                    if (!visitedVersions.ContainsKey(dep.Key))
                    {
                        queue.Enqueue((dep.Key, dep.Value));
                    }
                }
            }

            // Mark whether the import was a no-op (already loaded) for caller visibility.
            // perPackageResult is the empty result from the idempotent short-circuit when
            // ImportedResources == 0 && TotalResources == 0 was returned by LoadPackageInternalAsync.
            var alreadyLoaded = perPackageResult.ImportedResources == 0 && perPackageResult.TotalResources == 0;
            loadedSpecs.Add(alreadyLoaded ? $"{id}@{ver} (already loaded)" : $"{id}@{ver}");

            aggregatedTotal += perPackageResult.TotalResources;
            aggregatedImported += perPackageResult.ImportedResources;
            aggregatedUpdated += perPackageResult.UpdatedResources;
            aggregatedDuration += perPackageResult.Duration;
            foreach (var kv in perPackageResult.ResourcesByType)
            {
                aggregatedResourcesByType.TryGetValue(kv.Key, out var existing);
                aggregatedResourcesByType[kv.Key] = existing + kv.Value;
            }
        }

        _logger.LogInformation(
            "Closure load complete for {PackageId}@{Version} into tenant {TenantId}. " +
            "Visited {Count} package(s), imported {Imported} resource(s) total in {Duration}ms",
            packageId, version, tenantId, loadedSpecs.Count, aggregatedImported, aggregatedDuration.TotalMilliseconds);

        return new PackageImportResult
        {
            PackageId = packageId,
            PackageVersion = version,
            TotalResources = aggregatedTotal,
            ImportedResources = aggregatedImported,
            UpdatedResources = aggregatedUpdated,
            Duration = aggregatedDuration,
            ResourcesByType = aggregatedResourcesByType,
            LoadedPackages = loadedSpecs,
            SkippedPackages = skippedSpecs.Count > 0 ? skippedSpecs : null,
        };
    }

    /// <summary>
    /// Lists all currently loaded packages for a specific tenant.
    /// </summary>
    /// <param name="tenantId">Tenant ID for database selection (currently unused - Phase 1 limitation)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of (packageId, version) tuples</returns>
    public async Task<IReadOnlyList<(string PackageId, string Version)>> ListLoadedPackagesAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Tenant ID cannot be null or empty", nameof(tenantId));

        _logger.LogDebug("Listing loaded packages for tenant {TenantId}", tenantId);

        try
        {
            // NOTE: Phase 1 limitation - returns packages from global repository for all tenants
            // Phase 2: Will extend IFhirRepository with tenant-scoped PackageResources property
            var packages = await _packageRepository.ListLoadedPackagesAsync(cancellationToken);

            _logger.LogInformation("Found {Count} loaded packages for tenant {TenantId}", packages.Count, tenantId);

            return packages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list loaded packages for tenant {TenantId}", tenantId);
            throw;
        }
    }

    /// <summary>
    /// Unloads (deactivates) a package from a tenant's database, making its resources unavailable.
    /// </summary>
    /// <param name="tenantId">Tenant ID for database selection (currently unused - Phase 1 limitation)</param>
    /// <param name="packageId">Package ID</param>
    /// <param name="version">Package version</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of resources deactivated</returns>
    public async Task<int> UnloadPackageAsync(
        string tenantId,
        string packageId,
        string version,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Tenant ID cannot be null or empty", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("Package ID cannot be null or empty", nameof(packageId));
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version cannot be null or empty", nameof(version));

        _logger.LogInformation(
            "Unloading package {PackageId}@{Version} from tenant {TenantId}",
            packageId, version, tenantId);

        try
        {
            // NOTE: Phase 1 limitation - deactivates packages from global repository for all tenants
            // Phase 2: Will extend IFhirRepository with tenant-scoped PackageResources property
            var count = await _packageRepository.DeactivatePackageAsync(
                packageId, version, cancellationToken);

            _logger.LogInformation(
                "Package {PackageId}@{Version} unloaded from tenant {TenantId}. Deactivated {Count} resources",
                packageId, version, tenantId, count);

            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to unload package {PackageId}@{Version} from tenant {TenantId}",
                packageId, version, tenantId);
            throw;
        }
    }
}
