namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record ReindexOrchestrationState(
    bool Started,
    bool BarrierDelayCompleted,
    IReadOnlyList<string> IgnoredLifecycleEvents,
    IReadOnlyList<ReindexTenantState> Tenants)
{
    public long ProgressSequence { get; init; }
    public bool DebounceCompleted { get; init; }

    public static ReindexOrchestrationState Create(IReadOnlyList<int> tenantIds) =>
        new(
            false,
            false,
            [],
            tenantIds.Select(ReindexTenantState.Create).ToArray());
}
