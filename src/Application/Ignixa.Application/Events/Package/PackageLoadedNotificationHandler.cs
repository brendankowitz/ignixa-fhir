using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.Specification;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Events.Package;

/// <summary>
/// Handles IPackageLoaded events by publishing fresh conformance consumers and invalidating capability caches.
/// </summary>
public class PackageLoadedNotificationHandler : INotificationHandler<IPackageLoaded>, INotificationHandler<PackageLoadedEvent>
{
    private readonly ConformanceRefreshPublisher _refreshPublisher;
    private readonly ICapabilityCacheInvalidator _capabilityCacheInvalidator;
    private readonly ILogger<PackageLoadedNotificationHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PackageLoadedNotificationHandler"/> class.
    /// </summary>
    /// <param name="refreshPublisher">Conformance snapshot publisher</param>
    /// <param name="capabilityCacheInvalidator">Capability cache invalidation after schema publication.</param>
    /// <param name="logger">Logger instance</param>
    public PackageLoadedNotificationHandler(
        ConformanceRefreshPublisher refreshPublisher,
        ICapabilityCacheInvalidator capabilityCacheInvalidator,
        ILogger<PackageLoadedNotificationHandler> logger)
    {
        _refreshPublisher = refreshPublisher ?? throw new ArgumentNullException(nameof(refreshPublisher));
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

        await _refreshPublisher.RefreshCurrentAsync(cancellationToken);
        await _capabilityCacheInvalidator.InvalidateForTenantAsync(evt.TenantId, cancellationToken);

        _logger.LogInformation(
            "Conformance snapshot refreshed for {PackageId} (tenant {TenantId})",
            evt.PackageId, evt.TenantId);
    }
}
