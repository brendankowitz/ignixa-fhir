using Ignixa.Application.Features.Conformance;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;

namespace Ignixa.Api.Services;

/// <summary>
/// Inserts the <c>dbo.SearchParam</c> rows a tenant's definitions need, through the registry's cache so the
/// write path reads the same identities. FileSystem tenants have no SQL catalog.
/// </summary>
public sealed class SqlSearchParameterCatalogSynchronizer(SqlServerSearchIndexCacheRegistry cacheRegistry)
    : ISearchParameterCatalogSynchronizer
{
    public async Task SynchronizeAsync(
        TenantConfiguration tenant,
        ISearchParameterDefinitionManager definitions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(definitions);

        if (string.Equals(tenant.Storage.Type, "FileSystem", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var canonicals = definitions.AllSearchParameters
            .Where(parameter => parameter.Url is not null)
            .Select(parameter => parameter.Url!.ToString())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var cache = await cacheRegistry.GetOrCreateAsync(tenant.TenantId, cancellationToken);
        await cache.SyncSearchParametersToDatabaseAsync(canonicals, definitions, cancellationToken);
    }
}
