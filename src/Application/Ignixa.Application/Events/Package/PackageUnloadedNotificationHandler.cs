using Ignixa.Application.Features.Conformance;
using Ignixa.Specification;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Events.Package;

/// <summary>
/// Handles IPackageUnloaded events by publishing fresh conformance consumers.
/// </summary>
public class PackageUnloadedNotificationHandler : INotificationHandler<IPackageUnloaded>, INotificationHandler<PackageUnloadedEvent>
{
    private readonly ConformanceRefreshPublisher _refreshPublisher;
    private readonly ILogger<PackageUnloadedNotificationHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PackageUnloadedNotificationHandler"/> class.
    /// </summary>
    /// <param name="refreshPublisher">Conformance snapshot publisher</param>
    /// <param name="logger">Logger instance</param>
    public PackageUnloadedNotificationHandler(
        ConformanceRefreshPublisher refreshPublisher,
        ILogger<PackageUnloadedNotificationHandler> logger)
    {
        _refreshPublisher = refreshPublisher ?? throw new ArgumentNullException(nameof(refreshPublisher));
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
    }
}
