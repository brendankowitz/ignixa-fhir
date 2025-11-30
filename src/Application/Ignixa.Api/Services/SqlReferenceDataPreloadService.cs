// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Ignixa.Api.Infrastructure;
using Ignixa.Application.Events.Startup;
using Ignixa.DataLayer.SqlEntityFramework;
using Ignixa.Domain.Abstractions;
using Medino;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ignixa.Api.Services;

/// <summary>
/// Handles TenantPackagePreloadCompletedEvent to pre-warm SQL reference data caches.
/// Preloads ResourceType and SearchParam mappings for each tenant to avoid
/// database hits on first bundle operation.
/// </summary>
public class SqlReferenceDataPreloadHandler(
    IServiceProvider serviceProvider,
    ILogger<SqlReferenceDataPreloadHandler> logger) : INotificationHandler<TenantPackagePreloadCompletedEvent>
{
    public async Task HandleAsync(TenantPackagePreloadCompletedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var overallStopwatch = Stopwatch.StartNew();
            var totalItemsLoaded = 0;
            logger.LogInformation(
                "Starting SQL reference data preload after package preload completed ({TenantCount} tenants, {PackagesLoaded} packages in {PreloadTime}ms)",
                notification.TenantCount,
                notification.PackagesLoaded,
                notification.ElapsedMilliseconds);

            using var scope = serviceProvider.CreateScope();
            var configStore = scope.ServiceProvider.GetRequiredService<ITenantConfigurationStore>();
            var repositoryFactory = scope.ServiceProvider.GetService<SqlEntityFrameworkRepositoryFactory>();
            var startupTiming = scope.ServiceProvider.GetRequiredService<StartupTimingDiagnostics>();

            if (repositoryFactory == null)
            {
                logger.LogDebug("SqlEntityFrameworkRepositoryFactory not available - skipping SQL reference data preload");
                return;
            }

            var tenants = await configStore.GetAllTenantsAsync(cancellationToken);

            if (tenants.Count == 0)
            {
                logger.LogWarning("No active tenants found for SQL reference data preload");
                return;
            }

            logger.LogInformation("Preloading SQL reference data for {Count} tenant(s)", tenants.Count);

            foreach (var tenant in tenants.Where(t => t.IsActive))
            {
                try
                {
                    using (startupTiming.StartPhase($"SqlReferenceData.Tenant{tenant.TenantId}"))
                    {
                        logger.LogDebug("Preloading SQL reference data for tenant {TenantId}", tenant.TenantId);

                        var referenceDataCache = await repositoryFactory.GetSearchIndexReferenceCacheAsync(
                            tenant.TenantId,
                            cancellationToken);

                        // Preload resource types (small dataset, always fully loaded)
                        await referenceDataCache.PreloadResourceTypesAsync();

                        // Preload search parameters (limited to 10,000 to avoid memory issues)
                        await referenceDataCache.PreloadSearchParamsAsync(maxRows: 10000);

                        var stats = referenceDataCache.GetStatistics();
                        logger.LogInformation(
                            "Preloaded SQL reference data for tenant {TenantId}: {ResourceTypes} resource types, {SearchParams} search params",
                            tenant.TenantId,
                            stats.ResourceTypeCount,
                            stats.SearchParamCount);
                        totalItemsLoaded += stats.ResourceTypeCount + stats.SearchParamCount;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Failed to preload SQL reference data for tenant {TenantId}. Data will be loaded lazily.",
                        tenant.TenantId);
                }
            }

            overallStopwatch.Stop();
            var itemsPerSecond = overallStopwatch.ElapsedMilliseconds > 0
                ? totalItemsLoaded / (overallStopwatch.ElapsedMilliseconds / 1000.0)
                : 0;

            logger.LogInformation(
                "SQL reference data preload completed: {TotalItems} items in {Elapsed:N0}ms ({ItemsPerSecond:N1} items/sec)",
                totalItemsLoaded,
                overallStopwatch.ElapsedMilliseconds,
                itemsPerSecond);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fatal error in SQL reference data preload handler");
            // Don't rethrow - allow server to continue
        }
    }
}
