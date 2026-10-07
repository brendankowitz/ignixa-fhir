namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed record ReindexTargetResolution(
    IReadOnlyList<ReindexTarget> Targets,
    IReadOnlyList<string> ResourceTypes)
{
    public bool HasWork => Targets.Count > 0 || ResourceTypes.Count > 0;
}
