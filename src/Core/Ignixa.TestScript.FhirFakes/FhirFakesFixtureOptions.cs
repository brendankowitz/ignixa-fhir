using Ignixa.FhirFakes;

namespace Ignixa.TestScript.FhirFakes;

internal sealed record FhirFakesFixtureOptions
{
    public required string ResourceType { get; init; }
    public int? Seed { get; init; }
    public GenerationDensity? Density { get; init; }
    public ClinicalDomain? Theme { get; init; }
    public string? Profile { get; init; }
    public string? Tag { get; init; }
    public FhirFakesPatientOptions? Patient { get; init; }
    public FhirFakesEdgeCaseOptions? EdgeCases { get; init; }
}
