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
    private readonly ConformanceRefresher _conformanceRefresher;
    private readonly ILogger<PackageUnloadedNotificationHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PackageUnloadedNotificationHandler"/> class.
    /// </summary>
    /// <param name="conformanceRefresher">Conformance definitions refresher</param>
    /// <param name="logger">Logger instance</param>
    public PackageUnloadedNotificationHandler(
        ConformanceRefresher conformanceRefresher,
        ILogger<PackageUnloadedNotificationHandler> logger)
    {
        _conformanceRefresher = conformanceRefresher ?? throw new ArgumentNullException(nameof(conformanceRefresher));
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

        try
        {
            await _conformanceRefresher.RefreshAsync(force: true, CancellationToken.None);
        }
        catch (ConformanceConsumerRefreshException exception)
        {
            _logger.LogWarning(
                exception,
                "Package {PackageId}@{Version} unloaded durably, but local conformance refresh is deferred",
                evt.PackageId,
                evt.PackageVersion);
            ConformanceMetrics.RecordConsumerRefreshFailure("package-unload");
            return;
        }

        _logger.LogInformation(
            "Conformance snapshot refreshed for unloaded {PackageId} (tenant {TenantId})",
            evt.PackageId, evt.TenantId);
    }
}
