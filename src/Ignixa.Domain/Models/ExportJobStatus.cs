namespace Ignixa.Domain.Models;

/// <summary>
/// Represents the status of a bulk export job.
/// </summary>
public enum ExportJobStatus
{
    /// <summary>
    /// Job has been queued but not yet started.
    /// </summary>
    Queued = 0,

    /// <summary>
    /// Job is currently in progress.
    /// </summary>
    InProgress = 1,

    /// <summary>
    /// Job completed successfully.
    /// </summary>
    Completed = 2,

    /// <summary>
    /// Job failed due to an error.
    /// </summary>
    Failed = 3,

    /// <summary>
    /// Job was cancelled by the user.
    /// </summary>
    Cancelled = 4,
}
