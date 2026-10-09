using Ignixa.Domain.Abstractions;

namespace Ignixa.Application.BackgroundOperations.Reindex;

internal static class ReindexStoreExtensions
{
    public static async Task<IReindexStore> GetReindexStoreAsync(
        this IFhirRepositoryFactory repositoryFactory,
        int tenantId,
        CancellationToken cancellationToken)
    {
        var repository = await repositoryFactory.GetRepositoryAsync(tenantId, cancellationToken);
        return repository as IReindexStore
            ?? throw new ReindexProviderNotSupportedException(tenantId);
    }
}
