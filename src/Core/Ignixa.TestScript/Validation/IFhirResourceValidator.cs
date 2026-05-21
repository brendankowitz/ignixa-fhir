using System.Text.Json.Nodes;

namespace Ignixa.TestScript.Validation;

public interface IFhirResourceValidator
{
    Task<ValidationResult> ValidateAsync(
        JsonNode resource,
        string? profileCanonical,
        CancellationToken cancellationToken);
}
