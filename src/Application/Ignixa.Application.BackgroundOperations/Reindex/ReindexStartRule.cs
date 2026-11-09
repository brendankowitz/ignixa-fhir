namespace Ignixa.Application.BackgroundOperations.Reindex;

/// <summary>
/// Decides whether automatic reindexing needs a new job: some parameter became Pending (activated, or committed
/// from Staged by a transition) after the target event of the latest finished job (Completed, Failed or
/// Cancelled), or no job has finished yet. A job planned at that event saw every earlier Pending parameter.
/// </summary>
/// <remarks>
/// A finished job's own Pending parameters therefore never restart on their own, so a failing job cannot
/// loop. A later activation restarts them together with the parameters it adds, and a manual
/// <c>$reindex</c> does not consult this rule at all.
/// </remarks>
internal static class ReindexStartRule
{
    public static bool RequiresJob(
        IEnumerable<long> pendingSinceEventIds,
        long? latestFinishedTargetEventId) =>
        pendingSinceEventIds.Any(pendingSinceEventId =>
            latestFinishedTargetEventId is not { } target || pendingSinceEventId > target);
}
