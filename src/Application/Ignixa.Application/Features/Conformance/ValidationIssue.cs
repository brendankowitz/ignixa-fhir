namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// An issue found while activating a package; <see cref="Code"/> is a stable machine-readable identifier.
/// </summary>
public record ValidationIssue(
    string Code,
    string Message,
    string? ResourceType = null,
    string? ParameterCode = null,
    ActivationIssueSeverity Severity = ActivationIssueSeverity.Error)
{
    /// <summary>
    /// The FHIR issue-severity code of <see cref="Severity"/>.
    /// </summary>
    public string SeverityCode => Severity switch
    {
        ActivationIssueSeverity.Error => "error",
        ActivationIssueSeverity.Warning => "warning",
        ActivationIssueSeverity.Information => "information",
        _ => throw new InvalidOperationException($"Unknown activation issue severity {Severity}."),
    };

    public static ValidationIssue Warning(string code, string message) =>
        new(code, message, Severity: ActivationIssueSeverity.Warning);

    public static ValidationIssue Information(string code, string message) =>
        new(code, message, Severity: ActivationIssueSeverity.Information);
}
