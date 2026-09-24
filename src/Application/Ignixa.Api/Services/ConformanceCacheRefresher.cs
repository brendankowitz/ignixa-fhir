using Ignixa.Application.Features.Search;
using Ignixa.Application.Features.Specification;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Domain.Abstractions;
using Ignixa.Serialization;

namespace Ignixa.Api.Services;

/// <summary>
/// Refreshes local consumers of replayed conformance state without replaying package-load side effects.
/// The caller must hold the conformance activation lock.
/// </summary>
public sealed class ConformanceCacheRefresher(
    IFhirVersionContext fhirVersionContext,
    SqlServerSearchIndexCacheRegistry cacheRegistry,
    ITenantConfigurationStore tenantConfigurationStore,
    ICompositeSchemaProviderRegistry schemaProviderRegistry,
    ICapabilityCacheInvalidator capabilityCacheInvalidator)
{
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var tenants = await tenantConfigurationStore.GetAllTenantsAsync(cancellationToken);

        foreach (var tenant in tenants)
        {
            if (string.Equals(tenant.Storage.Type, "FileSystem", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var version = FhirSpecificationExtensions.FromVersionString(tenant.FhirVersion);
            var definitions = fhirVersionContext.GetSearchParameterDefinitionManager(version, tenant.TenantId);

            // AllSearchParameters reads current conformance state even when resource/code lookups are
            // warm. Populate the instances held by existing writers before publishing new indexers.
            var canonicals = definitions.AllSearchParameters
                .Where(parameter => parameter.Url is not null)
                .Select(parameter => parameter.Url!.ToString())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var cache = await cacheRegistry.GetOrCreateAsync(tenant.TenantId, cancellationToken);
            await cache.SyncSearchParametersToDatabaseAsync(canonicals, definitions, cancellationToken);
        }

        foreach (var tenant in tenants)
        {
            await schemaProviderRegistry.InvalidateCachesForTenantImmediatelyAsync(tenant.TenantId, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        fhirVersionContext.InvalidateSearchParameterCaches();

        foreach (var tenant in tenants)
        {
            await capabilityCacheInvalidator.InvalidateForTenantAsync(tenant.TenantId, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
