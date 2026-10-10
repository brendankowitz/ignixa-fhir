using Ignixa.Application.Features.Conformance;
using Ignixa.Specification;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Events.Package;

/// <summary>
/// Handles IPackageLoaded events by publishing fresh conformance consumers.
/// </summary>
public class PackageLoadedNotificationHandler : INotificationHandler<IPackageLoaded>, INotificationHandler<PackageLoadedEvent>
{
    private readonly ConformanceRefresher _conformanceRefresher;
    private readonly ILogger<PackageLoadedNotificationHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PackageLoadedNotificationHandler"/> class.
    /// </summary>
    /// <param name="conformanceRefresher">Conformance definitions refresher</param>
    /// <param name="logger">Logger instance</param>
    public PackageLoadedNotificationHandler(
        ConformanceRefresher conformanceRefresher,
        ILogger<PackageLoadedNotificationHandler> logger)
    {
        _conformanceRefresher = conformanceRefresher ?? throw new ArgumentNullException(nameof(conformanceRefresher));
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
        try
        {
            if (evt.RequiresConformanceRefresh)
            {
                await _conformanceRefresher.RefreshAsync(force: true, CancellationToken.None);
            }
            else
            {
                await _conformanceRefresher.RefreshAsync(force: false, cancellationToken);
            }
        }
        catch (ConformanceConsumerRefreshException exception)
        {
            _logger.LogWarning(
                exception,
                "Package {PackageId}@{Version} loaded durably, but local conformance refresh is deferred",
                evt.PackageId,
                evt.PackageVersion);
            ConformanceMetrics.RecordConsumerRefreshFailure("package-load");
            return;
        }

        _logger.LogInformation(
            "Conformance snapshot refreshed for {PackageId} (tenant {TenantId})",
            evt.PackageId, evt.TenantId);
    }
}
