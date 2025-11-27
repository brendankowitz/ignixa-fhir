using Ignixa.Application.Features.Specification;
using Ignixa.Specification;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Events.Package;

/// <summary>
/// Handles IPackageUnloaded events to invalidate validation schema caches.
/// Ensures CompositeStructureDefinitionSummaryProvider removes unloaded profiles.
/// </summary>
public class PackageUnloadedNotificationHandler : INotificationHandler<IPackageUnloaded>
{
    private readonly ICompositeSchemaProviderRegistry _registry;
    private readonly ILogger<PackageUnloadedNotificationHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PackageUnloadedNotificationHandler"/> class.
    /// </summary>
    /// <param name="registry">Composite schema provider registry</param>
    /// <param name="logger">Logger instance</param>
    public PackageUnloadedNotificationHandler(
        ICompositeSchemaProviderRegistry registry,
        ILogger<PackageUnloadedNotificationHandler> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    /// <summary>
    /// Handles the PackageUnloaded event by invalidating validation caches.
    /// </summary>
    public async Task HandleAsync(IPackageUnloaded evt, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Handling PackageUnloaded event: {PackageId}@{Version} (tenant {TenantId})",
            evt.PackageId, evt.PackageVersion, evt.TenantId);

        // Invalidate validation schema caches for this tenant
        // This ensures CompositeStructureDefinitionSummaryProvider removes unloaded profiles
        await _registry.InvalidateCacheForPackageAsync(evt.PackageId, evt.TenantId, cancellationToken);

        _logger.LogInformation(
            "Validation cache invalidated for unloaded {PackageId} (tenant {TenantId})",
            evt.PackageId, evt.TenantId);
    }
}
