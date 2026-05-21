namespace Ignixa.TestScript.Reporting;

public sealed record TestScriptReport
{
    public required string TestScriptName { get; init; }
    public required DateTimeOffset StartTime { get; init; }
    public required DateTimeOffset EndTime { get; init; }
    public TestPhaseResult? SetupResult { get; init; }
    public IReadOnlyList<TestCaseResult> TestResults { get; init; } = [];
    public TestPhaseResult? TeardownResult { get; init; }

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
                TestResults.Any(t => t.Outcome == TestScriptOutcome.Warning))
                return TestScriptOutcome.Warning;
            return TestScriptOutcome.Pass;
        }
    }
}
