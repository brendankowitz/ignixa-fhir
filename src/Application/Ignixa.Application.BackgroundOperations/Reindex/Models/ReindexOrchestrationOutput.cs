namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record ReindexOrchestrationOutput(
    bool Success,
    IReadOnlyList<ReindexTenantProgress> Tenants);
