namespace Ignixa.TestScript.Reporting;

public sealed record TestScriptReport
{
    public required string TestScriptName { get; init; }
    public required DateTimeOffset StartTime { get; init; }
    public required DateTimeOffset EndTime { get; init; }
    public TestPhaseResult? SetupResult { get; init; }
    public IReadOnlyList<TestCaseResult> TestResults { get; init; } = [];
    public TestPhaseResult? TeardownResult { get; init; }

    /// <summary>
    /// Aggregate outcome of the run. Teardown is never allowed to fail or error the overall result —
    /// FHIR does not score teardown — but a teardown error/fail is surfaced as <see cref="TestScriptOutcome.Warning"/>
    /// so a broken cleanup is visible rather than silently dropped.
    /// </summary>
    public TestScriptOutcome OverallOutcome
    {
        get
        {
            if (SetupResult?.Outcome is TestScriptOutcome.Error or TestScriptOutcome.Fail)
                return SetupResult.Outcome;
            if (TestResults.Any(t => t.Outcome == TestScriptOutcome.Error))
                return TestScriptOutcome.Error;
            if (TestResults.Any(t => t.Outcome == TestScriptOutcome.Fail))
                return TestScriptOutcome.Fail;
            if (SetupResult?.Outcome == TestScriptOutcome.Warning ||
                TeardownResult?.Outcome is TestScriptOutcome.Warning or TestScriptOutcome.Fail or TestScriptOutcome.Error ||
                TestResults.Any(t => t.Outcome == TestScriptOutcome.Warning))
                return TestScriptOutcome.Warning;
            return TestScriptOutcome.Pass;
        }
    }
}
