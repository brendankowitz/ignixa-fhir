namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed class ReindexDefinitionsNotReadyException(long definitionsEventId, long targetEventId)
    : Exception(
        $"Reindex definitions are at event {definitionsEventId}, behind target event {targetEventId}. Retry the activity.")
{
    public long DefinitionsEventId { get; } = definitionsEventId;

    public long TargetEventId { get; } = targetEventId;
}
