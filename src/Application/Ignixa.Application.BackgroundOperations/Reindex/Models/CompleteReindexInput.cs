using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record CompleteReindexInput(
    string JobId,
    long TargetEventId,
    IReadOnlyList<ReindexParameterDefinition> Targets,
    IReadOnlyList<ReindexTenantOutput> Tenants,
    IReadOnlyList<string> IgnoredLifecycleEvents)
{
    public string? FailureMessage { get; init; }
}
