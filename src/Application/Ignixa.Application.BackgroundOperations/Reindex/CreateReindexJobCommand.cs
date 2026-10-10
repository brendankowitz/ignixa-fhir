using Medino;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed record CreateReindexJobCommand : IRequest<CreateReindexJobResult>
{
    public int? MaximumNumberOfResourcesPerQuery { get; init; }
    public int? MaximumNumberOfResourcesPerWrite { get; init; }
    public int? MaximumConcurrency { get; init; }
    public int? QueryDelayIntervalInMilliseconds { get; init; }
    public string Trigger { get; init; } = "Manual";

    /// <summary>
    /// Automatic triggers (activation, reconciliation) apply <see cref="ReindexStartRule"/>; a manual request
    /// starts a job whenever none is active.
    /// </summary>
    public bool IsAutomatic => !Trigger.Equals("Manual", StringComparison.OrdinalIgnoreCase);
}
