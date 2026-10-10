namespace Ignixa.Application.BackgroundOperations.Reindex;

/// <summary>
/// Facts shared by every reindex job writer: a job's first Completed, Failed or Cancelled status is final.
/// </summary>
internal static class ReindexJobs
{
    public static bool IsTerminal(string status) =>
        status is "Completed" or "Failed" or "Cancelled";
}
