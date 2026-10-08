namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Starts or queues reindexing after an activation makes definitions pending.
/// The durable queue implementation is introduced with reindex automation.
/// </summary>
public interface IReindexTrigger
{
    Task<ReindexTriggerResult> RequestReindexAsync(
        string reason,
        CancellationToken cancellationToken);
}

public sealed record ReindexTriggerResult(
    string? JobId,
    bool Queued,
    string? Message);
