namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed record ReindexTarget(
    string Canonical,
    string Code,
    string ResourceType,
    int SearchParamId,
    long ActivationEventId,
    IReadOnlyList<string> AffectedResourceTypes);
