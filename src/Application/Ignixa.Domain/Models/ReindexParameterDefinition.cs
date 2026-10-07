namespace Ignixa.Domain.Models;

public sealed record ReindexParameterDefinition(
    string Canonical,
    string Code,
    string ResourceType,
    int SearchParamId,
    long ActivationEventId,
    IReadOnlyList<string> AffectedResourceTypes)
{
    public IReadOnlyList<string> ScheduledResourceTypes { get; init; } = AffectedResourceTypes;
}
