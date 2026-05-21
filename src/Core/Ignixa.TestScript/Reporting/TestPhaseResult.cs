namespace Ignixa.TestScript.Reporting;

public sealed record TestPhaseResult(
    IReadOnlyList<ActionResult> Actions,
    TestScriptOutcome Outcome);
