using Medino;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed record CreateReindexJobCommand : IRequest<CreateReindexJobResult>
{
    public int? MaximumNumberOfResourcesPerQuery { get; init; }
    public int? MaximumNumberOfResourcesPerWrite { get; init; }
    public int? MaximumConcurrency { get; init; }
    public int? QueryDelayIntervalInMilliseconds { get; init; }
    public IReadOnlyCollection<string>? TargetResourceTypes { get; init; }
    public string Trigger { get; init; } = "Manual";
}
