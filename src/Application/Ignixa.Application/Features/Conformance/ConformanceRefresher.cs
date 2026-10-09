using System.Data.Common;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Domain.Abstractions;
using Ignixa.Serialization;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Rebuilds every tenant's conformance definitions off the activation lock and publishes them only while the
/// projection generation they were built from is still current.
/// </summary>
/// <remarks>
/// Refreshes are serialized. A forced refresh republishes the current generation even when it is already
/// published (package load and unload change package resources without advancing the projection); if it fails,
/// the next unforced refresh rebuilds instead of treating the generation as current.
/// </remarks>
public sealed class ConformanceRefresher(
    ConformanceState conformanceState,
    ISourceEventStore eventStore,
    IFhirVersionContext fhirVersionContext,
    ITenantConfigurationStore tenantConfigurationStore,
    ISearchParameterCatalogSynchronizer searchParameterCatalog,
    ICapabilityCacheInvalidator capabilityCacheInvalidator,
    ILogger<ConformanceRefresher> logger) : IDisposable
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    // Guarded by _refreshLock.
    private long _publishedGeneration = -1;

    // Written under _refreshLock; read lock-free by the sync tick through HasPendingRefresh.
    private volatile bool _forcePending;

    /// <summary>
    /// Gets whether a forced refresh failed and has not yet been retried successfully.
    /// </summary>
    public bool HasPendingRefresh => _forcePending;

    /// <summary>
    /// Applies any events this instance has not seen, then refreshes consumers to the resulting generation.
    /// </summary>
    public async Task SynchronizeAsync(CancellationToken cancellationToken)
    {
        using (await conformanceState.AcquireActivationLockAsync(cancellationToken))
        {
            await conformanceState.CatchUpWhileActivationLockHeldAsync(eventStore, cancellationToken);
        }

        await RefreshAsync(force: false, cancellationToken);
    }

    /// <summary>
    /// Publishes definitions for the current projection generation and returns that generation.
    /// </summary>
    /// <param name="force">
    /// Rebuild and republish even when the current generation is already published.
    /// </param>
    /// <exception cref="ConformanceConsumerRefreshException">
    /// A storage or configuration dependency failed; the projection itself is unaffected.
    /// </exception>
    public async Task<long> RefreshAsync(bool force, CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            while (true)
            {
                ConformanceStateSnapshot stateSnapshot;
                long generation;
                using (await conformanceState.AcquireActivationLockAsync(cancellationToken))
                {
                    generation = conformanceState.LastProcessedEventId;
                    if (!force && !_forcePending && generation <= _publishedGeneration)
                    {
                        return generation;
                    }

                    stateSnapshot = conformanceState.CreateSnapshot();
                }

                IReadOnlyList<TenantDefinitions> definitions;
                try
                {
                    definitions = await BuildAsync(stateSnapshot, generation, cancellationToken);
                }
                catch when (force)
                {
                    _forcePending = true;
                    throw;
                }

                using (await conformanceState.AcquireActivationLockAsync(cancellationToken))
                {
                    if (conformanceState.LastProcessedEventId != generation)
                    {
                        logger.LogInformation(
                            "Discarding conformance definitions for EventId {SnapshotEventId}; projection advanced to EventId {CurrentEventId}",
                            generation,
                            conformanceState.LastProcessedEventId);
                        continue;
                    }

                    foreach (var tenant in definitions)
                    {
                        fhirVersionContext.PublishConformanceDefinitionsSnapshot(
                            tenant.Version,
                            tenant.TenantId,
                            tenant.Snapshot);
                    }

                    _publishedGeneration = generation;
                    _forcePending = false;
                }

                // Published definitions are visible; the caller's cancellation must not leave the
                // capability statements describing the previous generation.
                foreach (var tenant in definitions)
                {
                    await capabilityCacheInvalidator.InvalidateForTenantAsync(
                        tenant.TenantId,
                        CancellationToken.None);
                }

                return generation;
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Dispose()
    {
        _refreshLock.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<IReadOnlyList<TenantDefinitions>> BuildAsync(
        ConformanceStateSnapshot stateSnapshot,
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            var tenants = await tenantConfigurationStore.GetAllTenantsAsync(cancellationToken);
            var definitions = new List<TenantDefinitions>(tenants.Count);
            foreach (var tenant in tenants)
            {
                var version = FhirSpecificationExtensions.FromVersionString(tenant.FhirVersion);
                var snapshot = fhirVersionContext.CreateConformanceDefinitionsSnapshot(
                    version,
                    tenant.TenantId,
                    stateSnapshot,
                    generation);
                await searchParameterCatalog.SynchronizeAsync(
                    tenant,
                    snapshot.ExtractionDefinitions,
                    cancellationToken);
                definitions.Add(new TenantDefinitions(version, tenant.TenantId, snapshot));
            }

            cancellationToken.ThrowIfCancellationRequested();
            return definitions;
        }
        catch (DbException exception)
        {
            throw new ConformanceConsumerRefreshException(
                "A database-backed conformance consumer could not be refreshed.",
                exception);
        }
        catch (IOException exception)
        {
            throw new ConformanceConsumerRefreshException(
                "A file-backed conformance consumer could not be refreshed.",
                exception);
        }
        catch (TimeoutException exception)
        {
            throw new ConformanceConsumerRefreshException(
                "A conformance consumer refresh timed out.",
                exception);
        }
    }

    private sealed record TenantDefinitions(
        FhirVersion Version,
        int TenantId,
        ConformanceDefinitionsSnapshot Snapshot);
}
