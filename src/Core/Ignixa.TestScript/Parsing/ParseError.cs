namespace Ignixa.TestScript.Parsing;

public sealed record ParseError(
    ParseSeverity Severity,
    string Message,
    string? Path = null);
