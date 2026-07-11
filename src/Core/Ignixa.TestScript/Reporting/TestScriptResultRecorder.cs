namespace Ignixa.TestScript.Reporting;

public sealed class TestScriptResultRecorder : ITestScriptResultRecorder
{
    private TestPhaseResult? _setupResult;
    private readonly List<TestCaseResult> _testResults = [];
    private TestPhaseResult? _teardownResult;
    private bool _isBuilt;

    private TestPhaseType _currentPhaseType;
    private string? _currentPhaseName;
    private string? _currentPhaseDescription;
    private bool _inPhase;
    private readonly List<ActionResult> _currentActions = [];

    public TestScriptOutcome? SetupOutcome => _setupResult?.Outcome;

    public void BeginPhase(TestPhaseType phase, string? name = null, string? description = null)
    {
        if (_inPhase)
            throw new InvalidOperationException(
                $"BeginPhase called while phase '{_currentPhaseType}' is still open. Call EndPhase first.");
        if (_isBuilt)
            throw new InvalidOperationException("Cannot record results after Build() has been called.");

        _currentPhaseType = phase;
        _currentPhaseName = name;
        _currentPhaseDescription = description;
        _inPhase = true;
        _currentActions.Clear();
    }

    public void RecordOperationResult(string? label, string? description, OperationOutcome outcome)
    {
        if (_isBuilt)
            throw new InvalidOperationException("Cannot record results after Build() has been called.");
        if (!_inPhase)
            throw new InvalidOperationException("RecordOperationResult called without an open phase. Call BeginPhase first.");
        var resultOutcome = outcome.Success ? TestScriptOutcome.Pass : TestScriptOutcome.Error;
        var exchange = outcome.Request is null && outcome.Response is null
            ? null
            : new HttpExchange(outcome.Request, outcome.Response);
        _currentActions.Add(new ActionResult(
            label,
            description,
            resultOutcome,
            outcome.ErrorMessage,
            outcome.Duration,
            TestActionKind.Operation,
            exchange));
    }

    public void RecordAssertionResult(string? label, string? description, AssertionOutcome outcome)
    {
        if (_isBuilt)
            throw new InvalidOperationException("Cannot record results after Build() has been called.");
        if (!_inPhase)
            throw new InvalidOperationException("RecordAssertionResult called without an open phase. Call BeginPhase first.");

        _currentActions.Add(new ActionResult(label, description, DetermineOutcome(outcome), outcome.Message));
    }

    public void RecordAssertionGroupResult(
        string groupId,
        string? label,
        string? description,
        AssertionOutcome outcome,
        IReadOnlyList<AssertionGroupMemberResult> members)
    {
        if (_isBuilt)
            throw new InvalidOperationException("Cannot record results after Build() has been called.");
        if (!_inPhase)
            throw new InvalidOperationException("RecordAssertionGroupResult called without an open phase. Call BeginPhase first.");

        _currentActions.Add(new ActionResult(
            label, description, DetermineOutcome(outcome), outcome.Message,
            GroupId: groupId, Members: members));
    }

    private static TestScriptOutcome DetermineOutcome(AssertionOutcome outcome)
    {
        if (!outcome.Applicable)
            return TestScriptOutcome.Skip;
        if (outcome.IsError)
            return TestScriptOutcome.Error;
        if (outcome.Passed)
            return TestScriptOutcome.Pass;
        if (outcome.WarningOnly)
            return TestScriptOutcome.Warning;
        return TestScriptOutcome.Fail;
    }

    public void EndPhase()
    {
        if (_isBuilt)
            throw new InvalidOperationException("Cannot record results after Build() has been called.");
        if (!_inPhase)
            throw new InvalidOperationException("EndPhase called without a matching BeginPhase.");

        var actions = _currentActions.ToList();
        var phaseOutcome = DeterminePhaseOutcome(actions);
        _currentActions.Clear();
        _inPhase = false;

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

    public void RecordSkippedTest(string name, string? description, string reason)
    {
        if (_inPhase)
            throw new InvalidOperationException(
                $"RecordSkippedTest called while phase '{_currentPhaseType}' is still open. Call EndPhase first.");
        if (_isBuilt)
            throw new InvalidOperationException("Cannot record results after Build() has been called.");

        _testResults.Add(new TestCaseResult(
            name,
            description,
            [new ActionResult(null, description, TestScriptOutcome.Skip, reason)],
            TestScriptOutcome.Skip));
    }

    public TestScriptReport Build(string testScriptName, DateTimeOffset startTime, DateTimeOffset endTime)
    {
        if (_inPhase)
            throw new InvalidOperationException(
                $"Build called while phase '{_currentPhaseType}' is still open. Call EndPhase first.");

        _isBuilt = true;
        return new()
        {
            TestScriptName = testScriptName,
            StartTime = startTime,
            EndTime = endTime,
            SetupResult = _setupResult,
            TestResults = _testResults.ToList(),
            TeardownResult = _teardownResult
        };
    }

    private static TestScriptOutcome DeterminePhaseOutcome(List<ActionResult> actions)
    {
        if (actions.Any(a => a.Outcome == TestScriptOutcome.Error))
            return TestScriptOutcome.Error;
        if (actions.Any(a => a.Outcome == TestScriptOutcome.Fail))
            return TestScriptOutcome.Fail;
        if (actions.Any(a => a.Outcome == TestScriptOutcome.Warning))
            return TestScriptOutcome.Warning;
        return TestScriptOutcome.Pass;
    }
}
