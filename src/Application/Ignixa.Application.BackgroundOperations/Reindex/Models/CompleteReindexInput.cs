namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record CompleteReindexInput(
    string JobId,
    long TargetEventId,
    IReadOnlyList<ReindexTarget> Targets,
    IReadOnlyList<ReindexTenantOutput> Tenants,
    IReadOnlyList<string> IgnoredLifecycleEvents)
{
    public string? FailureMessage { get; init; }
}
