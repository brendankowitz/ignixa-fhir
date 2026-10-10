namespace Ignixa.Application.BackgroundOperations.Reindex;

/// <summary>
/// What asked for a reindex job. Automatic kinds apply <see cref="ReindexStartRule"/>; a manual request
/// starts a job whenever none is active. The member name is what the job row stores and reports.
/// </summary>
public enum ReindexTriggerKind
{
    Manual,
    Activation,
    Reconciliation
}
