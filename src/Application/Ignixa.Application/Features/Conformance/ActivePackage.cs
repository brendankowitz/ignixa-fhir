namespace Ignixa.Application.Features.Conformance;

public record ActivePackage
{
    public required string PackageId { get; init; }
    public required string Version { get; init; }
    public required int ResourceCount { get; init; }
    public required DateTimeOffset ActivatedAt { get; init; }
}
