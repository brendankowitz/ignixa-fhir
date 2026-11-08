namespace Ignixa.Domain.Models;

public static class DefinitionsEventIdSelector
{
    public static long GetMinimum(IReadOnlyCollection<ResourceWrapper> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        return resources.Count == 0 ? 0 : resources.Min(resource => resource.DefinitionsEventId);
    }
}
