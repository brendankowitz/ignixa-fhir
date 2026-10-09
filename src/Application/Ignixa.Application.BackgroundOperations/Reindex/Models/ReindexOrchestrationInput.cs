using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record ReindexOrchestrationInput(
    string JobId,
    long TargetEventId,
    TimeSpan BarrierDelay,
    IReadOnlyList<int> TenantIds,
    IReadOnlyList<string> ResourceTypes,
    IReadOnlyList<ReindexParameterDefinition> Targets,
    ReindexJobParameters Parameters,
    TimeSpan? DrainWarningAfter = null,
    int ContinueAsNewThreshold = 2_000,
    ReindexOrchestrationState? State = null)
{
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan StartDebounce { get; init; }
    public TimeSpan StaleJobTimeout { get; init; } = TimeSpan.FromMinutes(30);

    public static ReindexOrchestrationInput CreateForTest(
        string jobId,
        long targetEventId,
        TimeSpan barrierDelay,
        IReadOnlyList<int> tenantIds) =>
        new(
            jobId,
            targetEventId,
            barrierDelay,
            tenantIds,
            ["Patient"],
            [],
            ReindexJobParameters.Create());
}
