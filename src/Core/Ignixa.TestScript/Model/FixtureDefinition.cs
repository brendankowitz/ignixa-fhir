using Ignixa.Serialization.SourceNodes;

namespace Ignixa.TestScript.Model;

public sealed record FixtureDefinition
{
    public required string Id { get; init; }
    public ResourceJsonNode? Resource { get; init; }
    public bool Autocreate { get; init; }
    public bool Autodelete { get; init; }
}
