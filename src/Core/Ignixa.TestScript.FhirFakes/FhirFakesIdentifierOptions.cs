namespace Ignixa.TestScript.FhirFakes;

internal sealed record FhirFakesIdentifierOptions
{
    public string? System { get; init; }
    public required string Value { get; init; }
}
