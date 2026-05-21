namespace Ignixa.TestScript.Model;

public sealed record TestScriptMetadata
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Url { get; init; }
    public string? Status { get; init; }
    public string? Version { get; init; }
}
