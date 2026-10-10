namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Starts reindexing after an activation makes definitions pending. This is the one Application-facing
/// contract of the reindex worker: the activation pipeline lives here while the trigger itself lives in
/// the background-operations project, which depends on this one.
/// </summary>
public interface IReindexTrigger
{
    /// <summary>
    /// Starts a job when none is active. Operational failures (storage, locking, the orchestration runtime)
    /// are logged and metered once and reported as a deferred result; programmer errors propagate.
    /// </summary>
    Task<ReindexTriggerResult> RequestReindexAsync(
        string reason,
        CancellationToken cancellationToken);
}
