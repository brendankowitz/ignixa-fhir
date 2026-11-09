using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex;

/// <summary>
/// The one place a reindex job row's status string becomes a <see cref="ReindexJobStatus"/> and back.
/// A job's first Completed, Failed or Cancelled status is final.
/// </summary>
internal static class ReindexJobs
{
    public static ReindexJobStatus GetStatus(this BackgroundJob<ReindexJobDefinition> job) =>
        Enum.TryParse<ReindexJobStatus>(job.Status, ignoreCase: true, out var status)
            ? status
            : throw new InvalidOperationException(
                $"Reindex job {job.JobId} has the unknown status '{job.Status}'.");

    public static void SetStatus(this BackgroundJob<ReindexJobDefinition> job, ReindexJobStatus status) =>
        job.Status = status.ToString();

    public static bool IsTerminal(this BackgroundJob<ReindexJobDefinition> job) =>
        job.GetStatus().IsTerminal();

    /// <summary>Writes a terminal status; the caller has already returned the job's parameters to Pending.</summary>
    public static void Close(
        this BackgroundJob<ReindexJobDefinition> job,
        ReindexJobStatus status,
        string? errorMessage,
        DateTimeOffset now)
    {
        job.SetStatus(status);
        job.EndDate = now;
        job.HeartbeatDate = now;
        job.ErrorMessage = errorMessage;
    }

    /// <summary>An active job whose heartbeat is older than <see cref="ReindexOptions.StaleJobTimeout"/>.</summary>
    public static bool IsStale(
        this BackgroundJob<ReindexJobDefinition> job,
        ReindexProgress? progress,
        ReindexOptions options,
        DateTimeOffset now)
    {
        // The barrier delay is one durable wait with no activity to heartbeat, so it does not count as silence.
        var allowedSilence = options.StaleJobTimeout +
            (progress?.Phase == ReindexPhase.BarrierDelay ? options.BarrierDelay : TimeSpan.Zero);
        return !job.IsTerminal() && now - job.HeartbeatDate > allowedSilence;
    }

    public static bool IsTerminal(this ReindexJobStatus status) =>
        status is ReindexJobStatus.Completed or ReindexJobStatus.Failed or ReindexJobStatus.Cancelled;
}
