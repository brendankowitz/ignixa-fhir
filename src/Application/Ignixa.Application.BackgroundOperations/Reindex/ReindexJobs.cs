namespace Ignixa.Application.BackgroundOperations.Reindex;

/// <summary>
/// Facts shared by every reindex job writer: jobs live in the global conformance tenant's job store, and a
/// job's first Completed, Failed or Cancelled status is final.
/// </summary>
internal static class ReindexJobs
{
    public const int GlobalTenantId = 1;

    public static bool IsTerminal(string status) =>
        status is "Completed" or "Failed" or "Cancelled";
}
