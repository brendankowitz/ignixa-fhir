using System.Diagnostics;
using Ignixa.Abstractions;
using Ignixa.Application.Events.Package;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Domain.Abstractions;
using Ignixa.Serialization;
using Ignixa.Specification;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Api.Events;

/// <summary>
/// Persists a package's search parameters to <c>dbo.SearchParam</c> when it is loaded, so the indexing
/// pipeline can find them.
/// <para>
/// <b>Why a failure here is not tolerable, unlike in the EF implementation this replaces.</b> That one
/// swallowed every exception, on the stated grounds that "the parameters will be loaded lazily on first
/// search". There is no lazy load on this data layer: the row generators read
/// <c>SearchParameterMappings</c> directly and skip a row whenever a parameter is absent from it, so a
/// parameter that never reached the database has <b>every one of its index rows silently dropped</b> —
/// writes report success and the resources are unfindable by that parameter. A swallowed failure is
/// therefore permanent data loss rather than a deferred cost, which is why this rethrows.
/// </para>
/// <para>
/// Rethrowing surfaces the failure to whoever published <c>PackageLoadedEvent</c> instead of leaving a
/// package looking loaded when it is only half-indexed. That is the trade this makes deliberately: a visibly
/// failed package load is recoverable, a silently unindexed one is not discovered until a search returns
/// nothing.
/// </para>
/// <para>
/// <b>Why this lives in the API layer rather than beside the cache it syncs.</b> It needs both
/// <c>PackageLoadedEvent</c> (Application) and the SqlServer cache registry (DataLayer). The EF handler this
/// replaces sat in the DataLayer and reached up into Application, inverting the layer graph;
/// <c>Ignixa.DataLayer.SqlServer</c> deliberately does not reference Application, so replicating that would
/// have meant adding the violation to a second project. The composition root is the one place that legally
/// sees both, and it already hosts notification handlers of this shape.
/// </para>
/// </summary>
public class PackageLoadedSearchParameterSyncHandler(
    IFhirVersionContext fhirVersionContext,
    SqlServerSearchIndexCacheRegistry cacheRegistry,
    ITenantConfigurationStore tenantConfigStore,
    ICapabilityCacheInvalidator capabilityCacheInvalidator,
    ILogger<PackageLoadedSearchParameterSyncHandler> logger) : INotificationHandler<PackageLoadedEvent>
{
    public async Task HandleAsync(PackageLoadedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        logger.LogInformation(
            "Handling PackageLoadedEvent for {PackageId}@{PackageVersion} in tenant {TenantId}",
            notification.PackageId,
            notification.PackageVersion,
            notification.TenantId);

        try
        {
            var stopwatch = Stopwatch.StartNew();

            var tenantConfig = await tenantConfigStore.GetTenantConfigurationAsync(
                notification.TenantId,
                cancellationToken);

            if (tenantConfig == null)
            {
                logger.LogWarning(
                    "Tenant {TenantId} not found. Skipping search parameter sync for {PackageId}@{PackageVersion}",
                    notification.TenantId,
                    notification.PackageId,
                    notification.PackageVersion);
                return;
            }

            var fhirVersion = FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);

            logger.LogDebug(
                "Using FHIR version {FhirVersion} for tenant {TenantId}",
                fhirVersion,
                notification.TenantId);

            var searchParamManager = fhirVersionContext.GetSearchParameterDefinitionManager(
                fhirVersion,
                notification.TenantId);

            var searchParamUrls = searchParamManager.AllSearchParameters
                .Where(sp => sp.Url != null)
                .Select(sp => sp.Url!.ToString())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            logger.LogInformation(
                "Found {Count} search parameters to sync for tenant {TenantId} after loading {PackageId}@{PackageVersion}",
                searchParamUrls.Count,
                notification.TenantId,
                notification.PackageId,
                notification.PackageVersion);

            // The registry's instance, not a fresh one: this has to be the cache the write path reads, or the
            // sync populates something nothing consults.
            var referenceDataCache = await cacheRegistry.GetOrCreateAsync(notification.TenantId, cancellationToken);

            var syncedCount = await referenceDataCache.SyncSearchParametersToDatabaseAsync(
                searchParamUrls,
                searchParamManager,
                cancellationToken);

            stopwatch.Stop();
            var paramsPerSecond = stopwatch.ElapsedMilliseconds > 0
                ? searchParamUrls.Count / (stopwatch.ElapsedMilliseconds / 1000.0)
                : 0;

            logger.LogInformation(
                "Successfully synced {SyncedCount} new search parameters ({TotalParams} total) for tenant {TenantId} in {ElapsedMs:N0}ms ({ParamsPerSecond:N1} params/sec) - {PackageId}@{PackageVersion}",
                syncedCount,
                searchParamUrls.Count,
                notification.TenantId,
                stopwatch.ElapsedMilliseconds,
                paramsPerSecond,
                notification.PackageId,
                notification.PackageVersion);

            await capabilityCacheInvalidator.InvalidateForTenantAsync(notification.TenantId, cancellationToken);

            logger.LogInformation(
                "Invalidated capability cache for tenant {TenantId} after loading {PackageId}@{PackageVersion}",
                notification.TenantId,
                notification.PackageId,
                notification.PackageVersion);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to sync search parameters for {PackageId}@{PackageVersion} in tenant {TenantId}. "
                + "Resources written for this tenant will be missing index rows for any parameter that did not "
                + "reach dbo.SearchParam, so this is surfaced rather than swallowed.",
                notification.PackageId,
                notification.PackageVersion,
                notification.TenantId);

            throw;
        }
    }
}
