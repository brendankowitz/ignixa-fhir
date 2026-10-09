using Ignixa.Domain.Models;
using Ignixa.Search.Definition;

namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Provisions a tenant's storage identities (for SQL, its <c>dbo.SearchParam</c> catalog) for a definition set
/// before <see cref="ConformanceRefresher"/> publishes it.
/// </summary>
/// <remarks>
/// This is the storage boundary of the refresh: the implementation lives with the data layer it writes to, so
/// the Application-layer refresher stays free of SQL types. Tenants whose storage has no catalog are a no-op.
/// </remarks>
public interface ISearchParameterCatalogSynchronizer
{
    Task SynchronizeAsync(
        TenantConfiguration tenant,
        ISearchParameterDefinitionManager definitions,
        CancellationToken cancellationToken);
}
