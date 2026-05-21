using System.Text.Json.Nodes;

namespace Ignixa.TestScript.Model;

public sealed record FixtureDefinition
{
    public required string Id { get; init; }
    public JsonNode? Resource { get; init; }
    public bool Autocreate { get; init; }
    public bool Autodelete { get; init; }
}
