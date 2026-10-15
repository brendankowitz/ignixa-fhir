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
    TimeSpan DrainWarningAfter,
    TimeSpan StaleJobTimeout,
    int ContinueAsNewThreshold,
    ReindexOrchestrationState? State = null);
