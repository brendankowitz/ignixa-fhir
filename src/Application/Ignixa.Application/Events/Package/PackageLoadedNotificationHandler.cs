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
    private readonly ConformanceRefreshPublisher _refreshPublisher;
    private readonly ILogger<PackageLoadedNotificationHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PackageLoadedNotificationHandler"/> class.
    /// </summary>
    /// <param name="refreshPublisher">Conformance snapshot publisher</param>
    /// <param name="logger">Logger instance</param>
    public PackageLoadedNotificationHandler(
        ConformanceRefreshPublisher refreshPublisher,
        ILogger<PackageLoadedNotificationHandler> logger)
    {
        _refreshPublisher = refreshPublisher ?? throw new ArgumentNullException(nameof(refreshPublisher));
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
                await _refreshPublisher.RefreshCurrentAsync(CancellationToken.None);
            }
            else
            {
                await _refreshPublisher.RefreshUntilCurrentAsync(cancellationToken);
            }
        }
        catch (ConformanceConsumerRefreshException exception)
        {
            _logger.LogWarning(
                exception,
                "Package {PackageId}@{Version} loaded durably, but local conformance refresh is deferred",
                evt.PackageId,
                evt.PackageVersion);
            ConformanceConsumerRefreshMetrics.RecordFailure("package-load");
            return;
        }

        _logger.LogInformation(
            "Conformance snapshot refreshed for {PackageId} (tenant {TenantId})",
            evt.PackageId, evt.TenantId);
    }
}
