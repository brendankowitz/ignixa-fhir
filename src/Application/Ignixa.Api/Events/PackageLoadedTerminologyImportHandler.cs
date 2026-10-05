using Ignixa.Application.Events.Package;
using Ignixa.Application.Events.Terminology;
using Ignixa.Domain.Abstractions;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Api.Events;

/// <summary>
/// Triggers terminology import for the CodeSystem, ValueSet and ConceptMap resources a freshly loaded
/// package brought with it, by publishing a <see cref="TerminologyImportTriggeredEvent"/>.
/// <para>
/// <b>Why this lives in the API layer rather than the data layer it reads through.</b> It needs both
/// <see cref="PackageLoadedEvent"/> (Application) and a repository (Domain), and it publishes through
/// Medino. The EF handler it replaces sat in <c>Ignixa.DataLayer.SqlEntityFramework</c> and reached up into
/// Application to do that, inverting the layer graph; <c>Ignixa.DataLayer.SqlServer</c> references neither
/// Application nor Medino, so the same handler cannot be written there at all. The composition root is the
/// one place that legally sees both, and already hosts
/// <see cref="PackageLoadedSearchParameterSyncHandler"/> for exactly this reason.
/// </para>
/// <para>
/// <b>The tenant on the event does not select the data.</b> The EF handler opened a tenant-scoped context to
/// run this query, which read as though package content were partitioned per tenant. It is not:
/// <c>dbo.PackageResource</c> has no tenant column, and <see cref="IPackageResourceRepository"/> is
/// registered against one fixed tenant for that reason. The tenant id is still carried on the published
/// event, because the import orchestration needs it for request-context purposes.
/// </para>
/// </summary>
public class PackageLoadedTerminologyImportHandler(
    IPackageResourceRepository packageResources,
    IMediator mediator,
    ILogger<PackageLoadedTerminologyImportHandler> logger) : INotificationHandler<PackageLoadedEvent>
{
    public async Task HandleAsync(PackageLoadedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        try
        {
            logger.LogInformation(
                "Processing PackageLoadedEvent for terminology import: {PackageId}@{PackageVersion}",
                notification.PackageId,
                notification.PackageVersion);

            var pending = await packageResources.ListPendingTerminologyImportsAsync(
                notification.PackageId, notification.PackageVersion, cancellationToken);

            var resourceIds = pending.SelectMany(p => p.PackageResourceIds).ToList();

            if (resourceIds.Count == 0)
            {
                logger.LogInformation(
                    "No terminology resources found to import for {PackageId}@{PackageVersion}",
                    notification.PackageId,
                    notification.PackageVersion);
                return;
            }

            logger.LogInformation(
                "Found {Count} terminology resources to import for {PackageId}@{PackageVersion}",
                resourceIds.Count,
                notification.PackageId,
                notification.PackageVersion);

            await mediator.PublishAsync(
                new TerminologyImportTriggeredEvent(
                    TenantId: notification.TenantId,
                    PackageId: notification.PackageId,
                    PackageVersion: notification.PackageVersion,
                    PackageResourceIds: resourceIds),
                cancellationToken);

            logger.LogInformation(
                "Published TerminologyImportTriggeredEvent for {Count} resources",
                resourceIds.Count);
        }
        catch (OperationCanceledException)
        {
            // Shutdown or a cancelled load is not an import failure; logging it as one would raise a false
            // alarm on every graceful stop.
            throw;
        }
        catch (Exception ex)
        {
            // Swallowed on purpose, matching the handler this replaces. The package itself is already stored
            // by the time this runs, and terminology import is recoverable without it: the bootstrap scan
            // re-offers anything still non-terminal on the next start. Failing the load instead would
            // discard a successful package install over a deferrable step.
            logger.LogError(
                ex,
                "Failed to trigger terminology import for {PackageId}@{PackageVersion}. The resources remain "
                + "pending and will be picked up by the next terminology import bootstrap scan.",
                notification.PackageId,
                notification.PackageVersion);
        }
    }
}
