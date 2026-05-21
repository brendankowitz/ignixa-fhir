namespace Ignixa.TestScript.Reporting;

public sealed class TestScriptResultRecorder : ITestScriptResultRecorder
{
    private TestPhaseResult? _setupResult;
    private readonly List<TestCaseResult> _testResults = [];
    private TestPhaseResult? _teardownResult;

    private TestPhaseType _currentPhaseType;
    private string? _currentPhaseName;
    private string? _currentPhaseDescription;
    private readonly List<ActionResult> _currentActions = [];

    public void BeginPhase(TestPhaseType phase, string? name = null, string? description = null)
    {
        _currentPhaseType = phase;
        _currentPhaseName = name;
        _currentPhaseDescription = description;
        _currentActions.Clear();
    }

    public void RecordOperationResult(string? label, string? description, OperationOutcome outcome)
    {
        var resultOutcome = outcome.Success ? TestScriptOutcome.Pass : TestScriptOutcome.Error;
        _currentActions.Add(new ActionResult(label, description, resultOutcome, outcome.ErrorMessage, outcome.Duration));
    }

    public void RecordAssertionResult(string? label, string? description, AssertionOutcome outcome)
    {
        TestScriptOutcome resultOutcome;
        if (outcome.Passed)
            resultOutcome = TestScriptOutcome.Pass;
        else if (outcome.WarningOnly)
            resultOutcome = TestScriptOutcome.Pass;
        else
            resultOutcome = TestScriptOutcome.Fail;

        _currentActions.Add(new ActionResult(label, description, resultOutcome, outcome.Message));
    }

    public void EndPhase()
    {
        var actions = _currentActions.ToList();
        var phaseOutcome = DeterminePhaseOutcome(actions);

        switch (_currentPhaseType)
        {
            case TestPhaseType.Setup:
                _setupResult = new TestPhaseResult(actions, phaseOutcome);
                break;
            case TestPhaseType.Teardown:
                _teardownResult = new TestPhaseResult(actions, phaseOutcome);
                break;
            case TestPhaseType.Test:
                _testResults.Add(new TestCaseResult(
                    _currentPhaseName ?? "Unnamed",
                    _currentPhaseDescription,
                    actions,
                    phaseOutcome));
                break;
        }
    }

    public TestScriptReport Build(string testScriptName, DateTimeOffset startTime, DateTimeOffset endTime) =>
        new()
        {
            TestScriptName = testScriptName,
            StartTime = startTime,
            EndTime = endTime,
            SetupResult = _setupResult,
            TestResults = _testResults,
            TeardownResult = _teardownResult
        };

    private static TestScriptOutcome DeterminePhaseOutcome(List<ActionResult> actions)
    {
        if (actions.Any(a => a.Outcome == TestScriptOutcome.Error))
            return TestScriptOutcome.Error;
        if (actions.Any(a => a.Outcome == TestScriptOutcome.Fail))
            return TestScriptOutcome.Fail;
        return TestScriptOutcome.Pass;
    }
}
