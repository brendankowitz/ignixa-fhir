using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex;

/// <summary>
/// The one place a reindex job row's status string becomes a <see cref="ReindexJobStatus"/> and back.
/// A job's first Completed, Failed or Cancelled status is final.
/// </summary>
internal static class ReindexJobs
{
    /// <summary>The stored spellings of the terminal statuses, for repository queries.</summary>
    public static readonly List<string> FinishedStatuses =
    [
        nameof(ReindexJobStatus.Completed),
        nameof(ReindexJobStatus.Failed),
        nameof(ReindexJobStatus.Cancelled)
    ];

    public static ReindexJobStatus GetStatus(this BackgroundJob<ReindexJobDefinition> job) =>
        Enum.TryParse<ReindexJobStatus>(job.Status, ignoreCase: true, out var status)
            ? status
            : throw new InvalidOperationException(
                $"Reindex job {job.JobId} has the unknown status '{job.Status}'.");

    public static void SetStatus(this BackgroundJob<ReindexJobDefinition> job, ReindexJobStatus status) =>
        job.Status = status.ToString();

    public static bool IsTerminal(this BackgroundJob<ReindexJobDefinition> job) =>
        job.GetStatus().IsTerminal();

    public static bool IsTerminal(this ReindexJobStatus status) =>
        status is ReindexJobStatus.Completed or ReindexJobStatus.Failed or ReindexJobStatus.Cancelled;
}
