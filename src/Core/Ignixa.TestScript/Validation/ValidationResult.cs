namespace Ignixa.TestScript.Validation;

public sealed record ValidationIssue(string Severity, string Message, string? Path = null);

public sealed record ValidationResult(
    bool IsValid,
    IReadOnlyList<ValidationIssue> Issues)
{
    public static ValidationResult Valid => new(true, []);
}
