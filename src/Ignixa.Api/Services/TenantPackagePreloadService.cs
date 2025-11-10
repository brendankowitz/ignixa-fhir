// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Admin;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Medino;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ignixa.Api.Services;

/// <summary>
/// Hosted service that preloads FHIR packages for all tenants at startup.
/// Reads package configuration from each tenant's TenantConfiguration.Packages.PreloadPackages.
/// Runs after all other services are initialized.
/// </summary>
public class TenantPackagePreloadService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TenantPackagePreloadService> _logger;

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
            _logger.LogInformation("Starting tenant package preload service...");

            using var scope = _serviceProvider.CreateScope();
            var configStore = scope.ServiceProvider.GetRequiredService<ITenantConfigurationStore>();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

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

            _logger.LogInformation("Preloading packages for {Count} tenant(s)", allTenants.Count);

            foreach (var tenant in allTenants)
            {
                if (!tenant.Packages.EnableAutoLoad || tenant.Packages.PreloadPackages.Count == 0)
                {
                    _logger.LogDebug(
                        "Tenant {TenantId} ({DisplayName}) has no packages to preload (EnableAutoLoad={EnableAutoLoad}, Count={Count})",
                        tenant.TenantId,
                        tenant.DisplayName,
                        tenant.Packages.EnableAutoLoad,
                        tenant.Packages.PreloadPackages.Count);
                    continue;
                }

                _logger.LogInformation(
                    "Preloading {Count} package(s) for tenant {TenantId} ({DisplayName})",
                    tenant.Packages.PreloadPackages.Count,
                    tenant.TenantId,
                    tenant.DisplayName);

                foreach (var packageRef in tenant.Packages.PreloadPackages)
                {
                    try
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

                        _logger.LogInformation(
                            "Loading package {PackageId}@{Version} for tenant {TenantId}",
                            packageId,
                            version,
                            tenant.TenantId);

                        var command = new LoadPackageCommand(tenant.TenantId.ToString(), packageId, version);
                        var result = await mediator.SendAsync(command, stoppingToken);

                        _logger.LogInformation(
                            "Successfully loaded {PackageId}@{Version} for tenant {TenantId}. Imported {Count} resources",
                            packageId,
                            version,
                            tenant.TenantId,
                            result.ImportedResources);
                    }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning(
                            ex,
                            "Package {PackageRef} not found in NPM registry for tenant {TenantId}. Skipping.",
                            packageRef,
                            tenant.TenantId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Error loading package {PackageRef} for tenant {TenantId}. Continuing with next package.",
                            packageRef,
                            tenant.TenantId);
                    }
                }
            }

            _logger.LogInformation("Tenant package preload service completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in tenant package preload service");
            // Don't rethrow - allow server to continue even if package preload fails
        }
    }
}
