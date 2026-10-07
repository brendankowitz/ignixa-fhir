namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Starts or queues reindexing after an activation makes definitions pending.
/// The durable queue implementation is introduced with reindex automation.
/// </summary>
public interface IReindexTrigger
{
    Task RequestReindexAsync(string reason, CancellationToken cancellationToken);
}
