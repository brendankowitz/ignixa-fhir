namespace Ignixa.TestScript.Reporting;

public sealed record AssertionOutcome(
    bool Passed,
    bool WarningOnly,
    string? Message = null,
    bool IsError = false);
