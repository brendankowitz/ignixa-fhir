namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed class ReindexProviderNotSupportedException(int tenantId)
    : Exception($"Tenant {tenantId} does not use a storage provider that supports reindexing.");
