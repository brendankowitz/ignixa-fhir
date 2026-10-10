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
    public TimeSpan StaleJobTimeout { get; init; } = TimeSpan.FromMinutes(30);
}
