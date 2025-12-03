// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Ignixa.Abstractions;
using Ignixa.Application.Events.Package;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Serialization;
using Ignixa.Specification;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlEntityFramework.Events;

/// <summary>
/// Handles PackageLoadedEvent to sync package search parameters to database.
/// CRITICAL: When packages (e.g., US Core) are loaded, their SearchParameters must be
/// persisted to the SearchParam table so the indexing pipeline can find them.
/// Without this sync, bundle processing will fail with "SearchParam URL not found" warnings.
/// </summary>
public class PackageLoadedSearchParameterSyncHandler : INotificationHandler<PackageLoadedEvent>
{
    private readonly IFhirVersionContext _fhirVersionContext;
    private readonly SqlEntityFrameworkRepositoryFactory _repositoryFactory;
    private readonly ITenantConfigurationStore _tenantConfigStore;
    private readonly ICapabilityCacheInvalidator _capabilityCacheInvalidator;
    private readonly ILogger<PackageLoadedSearchParameterSyncHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PackageLoadedSearchParameterSyncHandler"/> class.
    /// </summary>
    /// <param name="fhirVersionContext">FHIR version context for getting search parameter managers.</param>
    /// <param name="repositoryFactory">Repository factory for getting tenant-specific reference data cache.</param>
    /// <param name="tenantConfigStore">Tenant configuration store for getting tenant FHIR version.</param>
    /// <param name="capabilityCacheInvalidator">Cache invalidator for capability statements.</param>
    /// <param name="logger">Logger instance.</param>
    public PackageLoadedSearchParameterSyncHandler(
        IFhirVersionContext fhirVersionContext,
        SqlEntityFrameworkRepositoryFactory repositoryFactory,
        ITenantConfigurationStore tenantConfigStore,
        ICapabilityCacheInvalidator capabilityCacheInvalidator,
        ILogger<PackageLoadedSearchParameterSyncHandler> logger)
    {
        _fhirVersionContext = fhirVersionContext ?? throw new ArgumentNullException(nameof(fhirVersionContext));
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _tenantConfigStore = tenantConfigStore ?? throw new ArgumentNullException(nameof(tenantConfigStore));
        _capabilityCacheInvalidator = capabilityCacheInvalidator ?? throw new ArgumentNullException(nameof(capabilityCacheInvalidator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles the PackageLoadedEvent by syncing package search parameters to database.
    /// </summary>
    public async Task HandleAsync(PackageLoadedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        _logger.LogInformation(
            "Handling PackageLoadedEvent for {PackageId}@{PackageVersion} in tenant {TenantId}",
            notification.PackageId,
            notification.PackageVersion,
            notification.TenantId);

        try
        {
            var stopwatch = Stopwatch.StartNew();

            // Get tenant configuration to determine FHIR version
            var tenantConfig = await _tenantConfigStore.GetTenantConfigurationAsync(
                notification.TenantId,
                cancellationToken);

            if (tenantConfig == null)
            {
                _logger.LogWarning(
                    "Tenant {TenantId} not found. Skipping search parameter sync for {PackageId}@{PackageVersion}",
                    notification.TenantId,
                    notification.PackageId,
                    notification.PackageVersion);
                return;
            }

            // Convert tenant's FHIR version string to enum (e.g., "4.0" -> FhirVersion.R4)
            var fhirVersion = FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);

            _logger.LogDebug(
                "Using FHIR version {FhirVersion} for tenant {TenantId}",
                fhirVersion,
                notification.TenantId);

            // Get search parameter definition manager for this tenant
            var searchParamManager = _fhirVersionContext.GetSearchParameterDefinitionManager(
                fhirVersion,
                notification.TenantId);

            // Get all search parameters (includes base + package parameters)
            var allSearchParams = searchParamManager.AllSearchParameters;

            // Extract all canonical URLs (these need to be in the database for indexing)
            // Note: MergeAllSearchParameters now deduplicates, so .Distinct() is redundant but harmless
            var searchParamUrls = allSearchParams
                .Where(sp => sp.Url != null)
                .Select(sp => sp.Url!.ToString())
                .Distinct()
                .ToList();

            _logger.LogInformation(
                "Found {Count} search parameters to sync for tenant {TenantId} after loading {PackageId}@{PackageVersion}",
                searchParamUrls.Count,
                notification.TenantId,
                notification.PackageId,
                notification.PackageVersion);

            // Get tenant-specific reference data cache
            var referenceDataCache = await _repositoryFactory.GetSearchIndexReferenceCacheAsync(
                notification.TenantId,
                cancellationToken);

            // Sync search parameters to database (pass manager for OverridesUrl aliasing)
            var syncedCount = await referenceDataCache.SyncSearchParametersToDatabase(
                searchParamUrls,
                searchParamManager);

            stopwatch.Stop();
            var paramsPerSecond = stopwatch.ElapsedMilliseconds > 0
                ? searchParamUrls.Count / (stopwatch.ElapsedMilliseconds / 1000.0)
                : 0;

            _logger.LogInformation(
                "Successfully synced {SyncedCount} new search parameters ({TotalParams} total) for tenant {TenantId} in {ElapsedMs:N0}ms ({ParamsPerSecond:N1} params/sec) - {PackageId}@{PackageVersion}",
                syncedCount,
                searchParamUrls.Count,
                notification.TenantId,
                stopwatch.ElapsedMilliseconds,
                paramsPerSecond,
                notification.PackageId,
                notification.PackageVersion);

            // Invalidate capability statement cache for this tenant
            // This ensures metadata endpoint reflects the newly loaded search parameters
            await _capabilityCacheInvalidator.InvalidateForTenantAsync(notification.TenantId, cancellationToken);

            _logger.LogInformation(
                "Invalidated capability cache for tenant {TenantId} after loading {PackageId}@{PackageVersion}",
                notification.TenantId,
                notification.PackageId,
                notification.PackageVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to sync search parameters for {PackageId}@{PackageVersion} in tenant {TenantId}",
                notification.PackageId,
                notification.PackageVersion,
                notification.TenantId);

            // Don't rethrow - allow package load to succeed even if sync fails
            // The parameters will be loaded lazily on first search
        }
    }
}
