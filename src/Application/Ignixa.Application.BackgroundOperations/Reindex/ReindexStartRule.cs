namespace Ignixa.Application.BackgroundOperations.Reindex;

/// <summary>
/// Decides whether automatic reindexing needs a new job: some Pending parameter was activated after the
/// target event of the latest finished job (Completed, Failed or Cancelled), or no job has finished yet.
/// </summary>
/// <remarks>
/// A finished job's own Pending parameters therefore never restart on their own, so a failing job cannot
/// loop. A later activation restarts them together with the parameters it adds, and a manual
/// <c>$reindex</c> does not consult this rule at all.
/// </remarks>
internal static class ReindexStartRule
{
    public static bool RequiresJob(
        IEnumerable<long> pendingActivationEventIds,
        long? latestFinishedTargetEventId)
    {
        ArgumentNullException.ThrowIfNull(pendingActivationEventIds);
        return pendingActivationEventIds.Any(activationEventId =>
            latestFinishedTargetEventId is not { } target || activationEventId > target);
    }
}
