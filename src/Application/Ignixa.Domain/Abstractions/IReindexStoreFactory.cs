namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Hands out the <see cref="IReindexStore"/> of a tenant, the way <see cref="IFhirRepositoryFactory"/> and
/// <see cref="ISearchServiceFactory"/> hand out its repository and search service.
/// </summary>
public interface IReindexStoreFactory
{
    /// <summary>
    /// Gets the reindex store for <paramref name="tenantId"/>.
    /// </summary>
    /// <exception cref="NotSupportedException">The tenant's storage provider cannot reindex.</exception>
    /// <exception cref="InvalidOperationException">The tenant does not exist or is inactive.</exception>
    Task<IReindexStore> GetReindexStoreAsync(int tenantId, CancellationToken cancellationToken);
}
