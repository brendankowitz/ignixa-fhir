namespace Ignixa.TestScript.Parsing;

public enum ParseSeverity
{
    Error,
    Warning
}

public sealed record ParseError(
    ParseSeverity Severity,
    string Message,
    string? Path = null);
