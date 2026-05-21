namespace Ignixa.TestScript.Validation;

public sealed record ValidationResult(
    bool IsValid,
    IReadOnlyList<ValidationIssue> Issues)
{
    public static ValidationResult Valid => new(true, []);
}
