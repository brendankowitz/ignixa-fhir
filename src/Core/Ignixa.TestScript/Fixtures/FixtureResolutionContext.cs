using Ignixa.Abstractions;

namespace Ignixa.TestScript.Fixtures;

public sealed record FixtureResolutionContext
{
    public required IFhirSchemaProvider Schema { get; init; }
    public string? ResourceType { get; init; }
    public string? BasePath { get; init; }
}
