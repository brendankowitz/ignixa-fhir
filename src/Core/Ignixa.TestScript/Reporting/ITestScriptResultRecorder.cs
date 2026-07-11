namespace Ignixa.TestScript.Reporting;

public interface ITestScriptResultRecorder
{
    TestScriptOutcome? SetupOutcome { get; }

    void RecordOperationResult(string? label, string? description, OperationOutcome outcome);
    void RecordAssertionResult(string? label, string? description, AssertionOutcome outcome);
    void RecordAssertionGroupResult(
        string groupId,
        string? label,
        string? description,
        AssertionOutcome outcome,
        IReadOnlyList<AssertionGroupMemberResult> members);
    void BeginPhase(TestPhaseType phase, string? name = null, string? description = null);
    void EndPhase();
    void RecordSkippedTest(string name, string? description, string reason);
    TestScriptReport Build(string testScriptName, DateTimeOffset startTime, DateTimeOffset endTime);
}
