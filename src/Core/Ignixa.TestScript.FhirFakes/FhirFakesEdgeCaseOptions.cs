namespace Ignixa.TestScript.FhirFakes;

internal sealed record FhirFakesEdgeCaseOptions
{
    public int? Seed { get; init; }
    public IReadOnlyList<string> Selectors { get; init; } = [];
}
