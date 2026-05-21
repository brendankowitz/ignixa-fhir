namespace Ignixa.TestScript.Validation;

public sealed record ValidationIssue(string Severity, string Message, string? Path = null);
