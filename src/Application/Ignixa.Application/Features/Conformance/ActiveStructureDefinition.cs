namespace Ignixa.Application.Features.Conformance;

public record ActiveStructureDefinition
{
    public required string Canonical { get; init; }
    public required string Type { get; init; }
    public required string Kind { get; init; }
    public required string SourcePackage { get; init; }
    public required string SnapshotJson { get; init; }
}
