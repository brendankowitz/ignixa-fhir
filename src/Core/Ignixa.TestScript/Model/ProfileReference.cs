namespace Ignixa.TestScript.Model;

public sealed record ProfileReference
{
    public required string Id { get; init; }
    public required string Canonical { get; init; }
}
