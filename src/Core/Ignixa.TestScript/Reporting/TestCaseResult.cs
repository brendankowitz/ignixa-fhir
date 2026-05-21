namespace Ignixa.TestScript.Reporting;

public sealed record TestCaseResult(
    string Name,
    string? Description,
    IReadOnlyList<ActionResult> Actions,
    TestScriptOutcome Outcome);
