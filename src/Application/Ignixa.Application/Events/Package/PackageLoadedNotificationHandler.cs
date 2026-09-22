using Ignixa.Application.Features.Specification;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.Specification;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Events.Package;

/// <summary>
/// Handles IPackageLoaded events to invalidate validation schema caches.
/// Ensures CompositeStructureDefinitionSummaryProvider picks up newly loaded profiles.
/// </summary>
public class PackageLoadedNotificationHandler : INotificationHandler<IPackageLoaded>, INotificationHandler<PackageLoadedEvent>
{
    private readonly ICompositeSchemaProviderRegistry _registry;
    private readonly ICapabilityCacheInvalidator _capabilityCacheInvalidator;
    private readonly ILogger<PackageLoadedNotificationHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PackageLoadedNotificationHandler"/> class.
    /// </summary>
    /// <param name="registry">Composite schema provider registry</param>
    /// <param name="capabilityCacheInvalidator">Capability cache invalidation after schema publication.</param>
    /// <param name="logger">Logger instance</param>
    public PackageLoadedNotificationHandler(
        ICompositeSchemaProviderRegistry registry,
        ICapabilityCacheInvalidator capabilityCacheInvalidator,
        ILogger<PackageLoadedNotificationHandler> logger)
    {
        _registry = registry;
        _capabilityCacheInvalidator = capabilityCacheInvalidator;
        _logger = logger;
    }

    public Task HandleAsync(PackageLoadedEvent evt, CancellationToken cancellationToken)
        => HandleAsync((IPackageLoaded)evt, cancellationToken);

    /// <summary>
    /// Handles the PackageLoaded event by invalidating validation caches.
    /// </summary>
    public async Task HandleAsync(IPackageLoaded evt, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Handling PackageLoaded event: {PackageId}@{Version} (tenant {TenantId})",
            evt.PackageId, evt.PackageVersion, evt.TenantId);

        // Invalidate validation schema caches for this tenant
        // This ensures CompositeStructureDefinitionSummaryProvider picks up new profiles
        await _registry.InvalidateCacheForPackageAsync(evt.PackageId, evt.TenantId, cancellationToken);
        await _capabilityCacheInvalidator.InvalidateForTenantAsync(evt.TenantId, cancellationToken);

        _logger.LogInformation(
            "Validation cache invalidated for {PackageId} (tenant {TenantId})",
            evt.PackageId, evt.TenantId);
    }
}
