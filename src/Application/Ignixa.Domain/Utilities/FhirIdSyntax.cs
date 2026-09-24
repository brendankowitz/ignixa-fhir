namespace Ignixa.Domain.Utilities;

/// <summary>
/// FHIR id lexical rules, shared by logical and opaque version identifiers.
/// URI routing and storage representation impose separate constraints.
/// </summary>
public static class FhirIdSyntax
{
    public static bool IsValid(string? value) =>
        value is { Length: > 0 and <= 64 } &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '.');
}
