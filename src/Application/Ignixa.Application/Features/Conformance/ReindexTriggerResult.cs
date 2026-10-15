namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// The outcome of an automatic reindex request.
/// </summary>
/// <param name="JobId">The job that was started, or the active job that will be followed up.</param>
/// <param name="Queued">A job was already active; periodic reconciliation starts a follow-up when it finishes.</param>
/// <param name="Message">Why no job was started, when none was.</param>
/// <param name="Deferred">The request failed operationally and periodic reconciliation will retry it.</param>
public sealed record ReindexTriggerResult(
    string? JobId,
    bool Queued,
    string? Message,
    bool Deferred = false);
