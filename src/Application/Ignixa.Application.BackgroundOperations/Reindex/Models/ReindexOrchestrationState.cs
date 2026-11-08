namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record ReindexOrchestrationState(
    bool Started,
    ReindexPhase Phase,
    IReadOnlyList<string> IgnoredLifecycleEvents,
    IReadOnlyList<ReindexTenantState> Tenants)
{
    /// <summary>Range waves scheduled since progress was last persisted.</summary>
    public int WavesSinceSnapshot { get; init; }

    public static ReindexOrchestrationState Create(IReadOnlyList<int> tenantIds) =>
        new(
            false,
            ReindexPhase.BarrierDelay,
            [],
            tenantIds.Select(ReindexTenantState.Create).ToArray());

    [Newtonsoft.Json.JsonIgnore]
    public bool AllTenantsCompleted => Tenants.All(tenant => tenant.IsCompleted);

    public ReindexProgress ToProgress() =>
        new(Phase)
        {
            Tenants = Tenants.Select(tenant => tenant.Progress).ToArray(),
            IgnoredLifecycleEvents = IgnoredLifecycleEvents
        };
}
