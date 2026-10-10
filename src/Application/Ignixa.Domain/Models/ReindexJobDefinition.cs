using Ignixa.Domain.Abstractions;

namespace Ignixa.Domain.Models;

public sealed class ReindexJobDefinition : IJobDefinition
{
    public int TenantId { get; init; } = 1;
    public required long TargetEventId { get; init; }
    public required IReadOnlyList<int> TenantIds { get; init; }
    public required IReadOnlyList<string> ResourceTypes { get; init; }
    public required IReadOnlyList<ReindexParameterDefinition> SearchParameters { get; init; }
    public required int MaximumNumberOfResourcesPerQuery { get; init; }
    public required int MaximumNumberOfResourcesPerWrite { get; init; }
    public required int MaximumConcurrency { get; init; }
    public required int QueryDelayIntervalInMilliseconds { get; init; }
    public required string Trigger { get; init; }
}
