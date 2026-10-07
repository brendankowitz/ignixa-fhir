using System.Data.Common;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Features.Specification;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Domain.Abstractions;
using Ignixa.Serialization;

namespace Ignixa.Api.Services;

/// <summary>
/// Builds local consumers from a detached conformance projection, then publishes them without I/O.
/// </summary>
public sealed class ConformanceCacheRefresher(
    IFhirVersionContext fhirVersionContext,
    SqlServerSearchIndexCacheRegistry cacheRegistry,
    ITenantConfigurationStore tenantConfigurationStore,
    ICapabilityCacheInvalidator capabilityCacheInvalidator) : IConformanceCacheRefresher
{
    public async Task<IConformanceConsumerSnapshot> BuildSnapshotAsync(
        ConformanceStateSnapshot stateSnapshot,
        long generation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stateSnapshot);

        try
        {
            var tenants = await tenantConfigurationStore.GetAllTenantsAsync(cancellationToken);

            var definitions = tenants.Select(tenant =>
            {
                var version = FhirSpecificationExtensions.FromVersionString(tenant.FhirVersion);
                return (
                    Version: version,
                    tenant.TenantId,
                    StorageType: tenant.Storage.Type,
                    Snapshot: fhirVersionContext.CreateConformanceDefinitionsSnapshot(
                        version,
                        tenant.TenantId,
                        stateSnapshot,
                        generation));
            }).ToList();

            foreach (var tenantDefinitions in definitions.Where(definition =>
                !string.Equals(definition.StorageType, "FileSystem", StringComparison.OrdinalIgnoreCase)))
            {
                var canonicals = tenantDefinitions.Snapshot.ExtractionDefinitions.AllSearchParameters
                    .Where(parameter => parameter.Url is not null)
                    .Select(parameter => parameter.Url!.ToString())
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                var cache = await cacheRegistry.GetOrCreateAsync(tenantDefinitions.TenantId, cancellationToken);
                await cache.SyncSearchParametersToDatabaseAsync(
                    canonicals,
                    tenantDefinitions.Snapshot.ExtractionDefinitions,
                    cancellationToken);
            }

            foreach (var tenant in tenants)
            {
                await capabilityCacheInvalidator.InvalidateForTenantAsync(tenant.TenantId, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new ConsumerSnapshot(generation, definitions);
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

    public void PublishSnapshot(IConformanceConsumerSnapshot snapshot)
    {
        var consumerSnapshot = snapshot as ConsumerSnapshot
            ?? throw new ArgumentException(
                $"Expected a {nameof(ConsumerSnapshot)}.",
                nameof(snapshot));

        foreach (var definitions in consumerSnapshot.Definitions)
        {
            fhirVersionContext.PublishConformanceDefinitionsSnapshot(
                definitions.Version,
                definitions.TenantId,
                definitions.Snapshot);
        }
    }

    private sealed record ConsumerSnapshot(
        long Generation,
        IReadOnlyList<(
            FhirVersion Version,
            int TenantId,
            string StorageType,
            ConformanceDefinitionsSnapshot Snapshot)> Definitions) : IConformanceConsumerSnapshot;
}
