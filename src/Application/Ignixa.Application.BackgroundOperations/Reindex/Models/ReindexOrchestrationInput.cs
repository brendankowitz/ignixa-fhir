namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record ReindexOrchestrationInput(
    string JobId,
    long TargetEventId,
    TimeSpan BarrierDelay,
    IReadOnlyList<int> TenantIds,
    IReadOnlyList<string> ResourceTypes,
    IReadOnlyList<ReindexTarget> Targets,
    ReindexJobParameters Parameters,
    TimeSpan? DrainWarningAfter = null,
    int ContinueAsNewThreshold = 2_000)
{
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
