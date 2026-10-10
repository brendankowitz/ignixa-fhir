namespace Ignixa.Domain.Exceptions;

/// <summary>
/// The stored job has changed or is terminal, so this update was superseded. Reload authoritative state
/// before merging progress again; retrying or requeuing terminal work requires a new job ID.
/// </summary>
public sealed class BackgroundJobUpdateConflictException(string jobId, string currentStatus)
    : Exception($"Background job {jobId} changed or is closed (status {currentStatus}); reload its authoritative state.")
{
    public string JobId { get; } = jobId;

    public string CurrentStatus { get; } = currentStatus;
}
