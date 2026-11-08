namespace Ignixa.Application.BackgroundOperations.Reindex;

/// <summary>
/// The lifecycle of a reindex job row. Its first Completed, Failed or Cancelled status is final.
/// The stored and reported spelling is the member name; <see cref="ReindexJobs"/> maps both ways.
/// </summary>
public enum ReindexJobStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}
