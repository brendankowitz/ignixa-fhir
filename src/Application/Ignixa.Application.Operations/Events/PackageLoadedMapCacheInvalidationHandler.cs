// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Events.Package;
using Ignixa.Application.Operations.Features.Transform;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Operations.Events;

/// <summary>
/// Handles PackageLoadedEvent to invalidate cached StructureMaps from the updated package.
///
/// When a package is loaded or updated:
/// 1. New StructureMaps may have been added
/// 2. Existing StructureMaps may have been updated
/// 3. Cached compiled MapExpressions are now stale
///
/// This handler ensures the MapRegistryCache is invalidated so fresh maps are loaded.
/// </summary>
public class PackageLoadedMapCacheInvalidationHandler : INotificationHandler<PackageLoadedEvent>
{
    private readonly MapRegistryCache _mapCache;
    private readonly ILogger<PackageLoadedMapCacheInvalidationHandler> _logger;

    public PackageLoadedMapCacheInvalidationHandler(
        MapRegistryCache mapCache,
        ILogger<PackageLoadedMapCacheInvalidationHandler> logger)
    {
        _mapCache = mapCache ?? throw new ArgumentNullException(nameof(mapCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task HandleAsync(PackageLoadedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Handling PackageLoadedEvent for {PackageId}#{Version} in tenant {TenantId}",
            notification.PackageId,
            notification.PackageVersion,
            notification.TenantId);

        // Get statistics before invalidation
        var statsBefore = _mapCache.GetStatistics();

        // Invalidate all maps from this package
        _mapCache.InvalidatePackage(notification.PackageId, notification.PackageVersion);

        // Log the impact
        var statsAfter = _mapCache.GetStatistics();
        var removedMaps = statsBefore.CachedMapCount - statsAfter.CachedMapCount;

        if (removedMaps > 0)
        {
            _logger.LogInformation(
                "Invalidated {RemovedCount} cached StructureMaps from package {PackageId}#{Version}",
                removedMaps,
                notification.PackageId,
                notification.PackageVersion);
        }
        else
        {
            _logger.LogDebug(
                "No cached StructureMaps found for package {PackageId}#{Version}",
                notification.PackageId,
                notification.PackageVersion);
        }

        return Task.CompletedTask;
    }
}
