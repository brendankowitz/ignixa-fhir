// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Ignixa.Api.Infrastructure;
using Ignixa.Application.Events.Startup;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Admin;
using Ignixa.Application.Features.Metadata;
using Ignixa.Application.Features.Metadata.Segments;
using Ignixa.Application.Features.Search;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Medino;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ignixa.Api.Services;

/// <summary>
/// Hosted service that preloads FHIR packages for all tenants at startup.
/// Loads packages configured in TenantConfiguration.Packages.PreloadPackages for each tenant.
/// Embedded packages (like SQL-on-FHIR ViewDefinition) are handled by EmbeddedPackageLoader
/// when referenced via "local.{packageId}@{version}" format in PreloadPackages configuration.
/// After loading packages, warms IFhirSchemaProvider, ISearchParameterDefinitionManager,
/// and CapabilityStatementService caches for each tenant and FHIR version to ensure
/// predictable first-request performance.
///
/// PERFORMANCE: Tenant operations are parallelized with configurable concurrency to reduce startup time.
/// </summary>
public class TenantPackagePreloadService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TenantPackagePreloadService> _logger;

    /// <summary>
    /// Maximum degree of parallelism for tenant operations.
    /// Balance between startup speed and resource usage (memory, CPU, NPM rate limits).
    /// </summary>
    private const int MaxParallelism = 4;

    public TenantPackagePreloadService(
        IServiceProvider serviceProvider,
        ILogger<TenantPackagePreloadService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var overallStopwatch = Stopwatch.StartNew();
            _logger.LogInformation("Starting package preload service (parallelism: {MaxParallelism})...", MaxParallelism);

            using var scope = _serviceProvider.CreateScope();
            var configStore = scope.ServiceProvider.GetRequiredService<ITenantConfigurationStore>();
            var startupTiming = scope.ServiceProvider.GetRequiredService<StartupTimingDiagnostics>();

            // Get all tenants (includes system partition and regular tenants)
            var tenants = await configStore.GetAllTenantsAsync(stoppingToken);
            var systemTenant = await configStore.GetTenantConfigurationAsync(SystemConstants.SystemPartitionId, stoppingToken);

            // Add system tenant to the list if it exists and is active
            var allTenants = tenants.ToList();
            if (systemTenant?.IsActive == true)
            {
                allTenants.Insert(0, systemTenant);
            }

            if (allTenants.Count == 0)
            {
                _logger.LogWarning("No active tenants found for package preload");
                return;
            }

            _logger.LogInformation("Preloading packages for {Count} tenant(s) in parallel", allTenants.Count);

            // Phase 1: Load packages for all tenants in parallel
            var totalResourcesImported = await LoadPackagesForTenantsAsync(allTenants, startupTiming, stoppingToken);

            // Phase 2: Warm providers for all tenants in parallel
            await WarmProvidersForTenantsAsync(allTenants, startupTiming, stoppingToken);

            // Phase 3: Warm capability statements for all tenants in parallel
            await WarmCapabilityStatementsForTenantsAsync(allTenants, startupTiming, stoppingToken);

            // Publish completion event for services that depend on package preload
            using var completionScope = _serviceProvider.CreateScope();
            var mediator = completionScope.ServiceProvider.GetRequiredService<IMediator>();
            var packagesLoaded = allTenants.Sum(t => t.Packages.PreloadPackages.Count);
            await mediator.PublishAsync(
                new TenantPackagePreloadCompletedEvent(
                    TenantCount: allTenants.Count,
                    PackagesLoaded: packagesLoaded,
                    ElapsedMilliseconds: overallStopwatch.ElapsedMilliseconds),
                stoppingToken);

            _logger.LogDebug("Published TenantPackagePreloadCompletedEvent");

            overallStopwatch.Stop();
            var resourcesPerSecond = overallStopwatch.ElapsedMilliseconds > 0
                ? totalResourcesImported / (overallStopwatch.ElapsedMilliseconds / 1000.0)
                : 0;

            _logger.LogInformation(
                "Package preload service completed: {TotalResources} resources imported in {Elapsed:N0}ms ({ResourcesPerSecond:N1} resources/sec)",
                totalResourcesImported,
                overallStopwatch.ElapsedMilliseconds,
                resourcesPerSecond);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in package preload service");
            // Don't rethrow - allow server to continue even if package preload fails
        }
    }

    /// <summary>
    /// Load packages for all tenants in parallel.
    /// Each tenant gets its own DI scope for thread safety.
    /// </summary>
    private async Task<int> LoadPackagesForTenantsAsync(
        List<TenantConfiguration> allTenants,
        StartupTimingDiagnostics startupTiming,
        CancellationToken stoppingToken)
    {
        var totalResourcesImported = 0;
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxParallelism,
            CancellationToken = stoppingToken
        };

        await Parallel.ForEachAsync(allTenants, parallelOptions, async (tenant, ct) =>
        {
            try
            {
                // Build list of packages to load for this tenant
                var packagesToLoad = new List<(string PackageId, string Version)>();

                // Add configured packages from tenant configuration
                if (tenant.Packages.EnableAutoLoad && tenant.Packages.PreloadPackages.Count > 0)
                {
                    foreach (var packageRef in tenant.Packages.PreloadPackages)
                    {
                        // Parse "packageId@version" format
                        var parts = packageRef.Split('@', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length != 2)
                        {
                            _logger.LogWarning(
                                "Invalid package reference format for tenant {TenantId}: '{PackageRef}'. Expected format: 'packageId@version'",
                                tenant.TenantId,
                                packageRef);
                            continue;
                        }

                        var packageId = parts[0].Trim();
                        var version = parts[1].Trim();
                        packagesToLoad.Add((packageId, version));
                    }
                }

                // Skip if no packages to load
                if (packagesToLoad.Count == 0)
                {
                    _logger.LogDebug(
                        "Tenant {TenantId} ({DisplayName}) has no packages to preload",
                        tenant.TenantId,
                        tenant.DisplayName);
                    return;
                }

                _logger.LogInformation(
                    "Preloading {Count} package(s) for tenant {TenantId} ({DisplayName})",
                    packagesToLoad.Count,
                    tenant.TenantId,
                    tenant.DisplayName);

                // Create a new scope for this tenant (thread safety)
                using var tenantScope = _serviceProvider.CreateScope();
                var mediator = tenantScope.ServiceProvider.GetRequiredService<IMediator>();

                // Load all packages for this tenant (sequential within tenant to avoid NPM rate limits)
                foreach (var (packageId, version) in packagesToLoad)
                {
                    try
                    {
                        using (startupTiming.StartPhase($"PackageLoad.T{tenant.TenantId}.{packageId}@{version}"))
                        {
                            _logger.LogInformation(
                                "Loading package {PackageId}@{Version} for tenant {TenantId}",
                                packageId,
                                version,
                                tenant.TenantId);

                            var command = new LoadPackageCommand(tenant.TenantId.ToString(), packageId, version);
                            var result = await mediator.SendAsync(command, ct);

                            _logger.LogInformation(
                                "Successfully loaded {PackageId}@{Version} for tenant {TenantId}. Imported {Count} resources",
                                packageId,
                                version,
                                tenant.TenantId,
                                result.ImportedResources);

                            Interlocked.Add(ref totalResourcesImported, result.ImportedResources);
                        }
                    }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("already loaded", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug(
                            "Package {PackageId}@{Version} already loaded for tenant {TenantId}",
                            packageId,
                            version,
                            tenant.TenantId);
                    }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning(
                            ex,
                            "Package {PackageId}@{Version} not found in NPM registry for tenant {TenantId}. Skipping.",
                            packageId,
                            version,
                            tenant.TenantId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Error loading package {PackageId}@{Version} for tenant {TenantId}. Continuing with next package.",
                            packageId,
                            version,
                            tenant.TenantId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to load packages for tenant {TenantId}. Continuing with other tenants.",
                    tenant.TenantId);
            }
        });

        return totalResourcesImported;
    }

    /// <summary>
    /// Warm schema providers and search parameter managers for all tenants in parallel.
    /// </summary>
    private async Task WarmProvidersForTenantsAsync(
        List<TenantConfiguration> allTenants,
        StartupTimingDiagnostics startupTiming,
        CancellationToken stoppingToken)
    {
        _logger.LogInformation("Warming schema providers and search parameter managers for {Count} tenant(s) in parallel", allTenants.Count);

        var activeTenants = allTenants.Where(x => x.IsActive).ToList();
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxParallelism,
            CancellationToken = stoppingToken
        };

        await Parallel.ForEachAsync(activeTenants, parallelOptions, async (tenant, ct) =>
        {
            try
            {
                using (startupTiming.StartPhase($"WarmProviders.Tenant{tenant.TenantId}"))
                {
                    // Create a new scope for this tenant (thread safety)
                    using var tenantScope = _serviceProvider.CreateScope();
                    var fhirVersionContext = tenantScope.ServiceProvider.GetRequiredService<IFhirVersionContext>();

                    _logger.LogDebug("Warming providers for tenant {TenantId}", tenant.TenantId);

                    // Warm schema providers for tenant's default FHIR version only
                    var fhirVersion = FhirSpecificationExtensions.FromVersionString(tenant.FhirVersion);

                    // Accessing the provider triggers eager loading of package StructureDefinitions
                    using (startupTiming.StartPhase($"SchemaProvider.Tenant{tenant.TenantId}"))
                    {
                        _ = fhirVersionContext.GetSchemaProvider(fhirVersion, tenant.TenantId);
                    }

                    // Accessing the manager triggers eager loading of package SearchParameters
                    using (startupTiming.StartPhase($"SearchParamManager.Tenant{tenant.TenantId}"))
                    {
                        _ = fhirVersionContext.GetSearchParameterDefinitionManager(fhirVersion, tenant.TenantId);
                    }

                    _logger.LogInformation(
                        "Warmed schema providers and search parameter managers for tenant {TenantId} ({DisplayName}) - FHIR {FhirVersion}",
                        tenant.TenantId,
                        tenant.DisplayName,
                        tenant.FhirVersion);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to warm providers for tenant {TenantId}. Providers will be loaded lazily on first request.",
                    tenant.TenantId);
            }

            await Task.CompletedTask; // Required for async lambda
        });
    }

    /// <summary>
    /// Warm capability statement caches for all tenants in parallel.
    /// </summary>
    private async Task WarmCapabilityStatementsForTenantsAsync(
        List<TenantConfiguration> allTenants,
        StartupTimingDiagnostics startupTiming,
        CancellationToken stoppingToken)
    {
        _logger.LogInformation("Warming capability statement caches for {Count} tenant(s) in parallel", allTenants.Count);

        var activeTenants = allTenants.Where(x => x.IsActive).ToList();
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxParallelism,
            CancellationToken = stoppingToken
        };

        await Parallel.ForEachAsync(activeTenants, parallelOptions, async (tenant, ct) =>
        {
            try
            {
                using (startupTiming.StartPhase($"CapabilityStatement.Tenant{tenant.TenantId}"))
                {
                    // Create a new scope for this tenant (thread safety)
                    using var tenantScope = _serviceProvider.CreateScope();
                    var capabilityService = tenantScope.ServiceProvider.GetRequiredService<CapabilityStatementService>();

                    var fhirVersion = FhirSpecificationExtensions.FromVersionString(tenant.FhirVersion);
                    var context = new CapabilityContext(
                        FhirVersion: fhirVersion,
                        TenantId: tenant.TenantId);

                    // This call warms both the capability statement cache AND the version hash cache
                    _ = await capabilityService.GetCapabilityStatementAsync(context, ct);

                    _logger.LogInformation(
                        "Warmed capability statement cache for tenant {TenantId} ({DisplayName}) - FHIR {FhirVersion}",
                        tenant.TenantId,
                        tenant.DisplayName,
                        tenant.FhirVersion);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to warm capability statement cache for tenant {TenantId}. Cache will be populated on first request.",
                    tenant.TenantId);
            }
        });
    }
}
