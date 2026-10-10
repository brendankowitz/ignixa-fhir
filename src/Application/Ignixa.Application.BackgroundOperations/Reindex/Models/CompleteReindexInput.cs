using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record CompleteReindexInput(
    string JobId,
    long TargetEventId,
    IReadOnlyList<ReindexParameterDefinition> Targets,
    IReadOnlyList<ReindexTenantProgress> Tenants,
    IReadOnlyList<string> IgnoredLifecycleEvents)
{
    public string? FailureMessage { get; init; }
}
