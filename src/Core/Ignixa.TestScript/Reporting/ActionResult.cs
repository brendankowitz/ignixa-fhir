namespace Ignixa.TestScript.Reporting;

public sealed record ActionResult(
    string? Label,
    string? Description,
    TestScriptOutcome Outcome,
    string? Message = null,
    TimeSpan Duration = default);
