using System.Text.Json.Nodes;

namespace Ignixa.TestScript.Validation;

public sealed class NoOpValidator : IFhirResourceValidator
{
    public Task<ValidationResult> ValidateAsync(
        JsonNode resource,
        string? profileCanonical,
        CancellationToken cancellationToken)
        => Task.FromResult(ValidationResult.Valid);
}
