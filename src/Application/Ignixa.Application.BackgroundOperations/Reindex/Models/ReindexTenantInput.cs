namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record ReindexTenantInput(
    string JobId,
    int TenantId,
    long TargetEventId,
    IReadOnlyList<string> ResourceTypes,
    ReindexJobParameters Parameters);
