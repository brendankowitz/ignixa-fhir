using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.Specification;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Events.Package;

/// <summary>
/// Handles IPackageUnloaded events by publishing fresh conformance consumers and invalidating capability caches.
/// </summary>
public class PackageUnloadedNotificationHandler : INotificationHandler<IPackageUnloaded>, INotificationHandler<PackageUnloadedEvent>
{
    private readonly ConformanceRefreshPublisher _refreshPublisher;
    private readonly ICapabilityCacheInvalidator _capabilityCacheInvalidator;
    private readonly ILogger<PackageUnloadedNotificationHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PackageUnloadedNotificationHandler"/> class.
    /// </summary>
    /// <param name="refreshPublisher">Conformance snapshot publisher</param>
    /// <param name="capabilityCacheInvalidator">Capability cache invalidator</param>
    /// <param name="logger">Logger instance</param>
    public PackageUnloadedNotificationHandler(
        ConformanceRefreshPublisher refreshPublisher,
        ICapabilityCacheInvalidator capabilityCacheInvalidator,
        ILogger<PackageUnloadedNotificationHandler> logger)
    {
        _refreshPublisher = refreshPublisher ?? throw new ArgumentNullException(nameof(refreshPublisher));
        _capabilityCacheInvalidator = capabilityCacheInvalidator ?? throw new ArgumentNullException(nameof(capabilityCacheInvalidator));
        _logger = logger;
    }

    public Task HandleAsync(PackageUnloadedEvent evt, CancellationToken cancellationToken)
        => HandleAsync((IPackageUnloaded)evt, cancellationToken);

    /// <summary>
    /// Handles the PackageUnloaded event by invalidating validation caches.
    /// </summary>
    public async Task HandleAsync(IPackageUnloaded evt, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Handling PackageUnloaded event: {PackageId}@{Version} (tenant {TenantId})",
            evt.PackageId, evt.PackageVersion, evt.TenantId);

        await _refreshPublisher.RefreshCurrentAsync(cancellationToken);

        _logger.LogInformation(
            "Conformance snapshot refreshed for unloaded {PackageId} (tenant {TenantId})",
            evt.PackageId, evt.TenantId);

        // Invalidate capability statement cache for this tenant
        // This ensures metadata endpoint reflects the removed search parameters/profiles
        await _capabilityCacheInvalidator.InvalidateForTenantAsync(evt.TenantId, cancellationToken);

        _logger.LogInformation(
            "Capability cache invalidated for unloaded {PackageId} (tenant {TenantId})",
            evt.PackageId, evt.TenantId);
    }
}
