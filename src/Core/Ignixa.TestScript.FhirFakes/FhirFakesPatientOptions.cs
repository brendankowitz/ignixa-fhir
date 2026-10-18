namespace Ignixa.TestScript.FhirFakes;

internal sealed record FhirFakesPatientOptions
{
    public string? GivenName { get; init; }
    public string? FamilyName { get; init; }
    public string? Gender { get; init; }
    public int? Age { get; init; }
    public string? BirthDate { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? ZipCode { get; init; }
    public bool? Active { get; init; }
    public decimal? Bmi { get; init; }
    public IReadOnlyList<FhirFakesIdentifierOptions> Identifiers { get; init; } = [];
}
