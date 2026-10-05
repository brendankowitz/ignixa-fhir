namespace Ignixa.Domain.Exceptions;

/// <summary>
/// The stored job is already terminal, so this update was superseded. Reload its authoritative state;
/// retrying or requeuing work requires a new job ID.
/// </summary>
public sealed class BackgroundJobUpdateConflictException(string jobId, string currentStatus)
    : Exception($"Background job {jobId} is already {currentStatus}; its terminal metadata cannot be overwritten.")
{
    public string JobId { get; } = jobId;

    public string CurrentStatus { get; } = currentStatus;
}
