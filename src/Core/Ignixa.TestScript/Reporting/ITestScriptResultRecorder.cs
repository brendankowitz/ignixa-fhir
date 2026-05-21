namespace Ignixa.TestScript.Reporting;

public enum TestPhaseType { Setup, Test, Teardown }

public interface ITestScriptResultRecorder
{
    void RecordOperationResult(string? label, string? description, OperationOutcome outcome);
    void RecordAssertionResult(string? label, string? description, AssertionOutcome outcome);
    void BeginPhase(TestPhaseType phase, string? name = null, string? description = null);
    void EndPhase();
    TestScriptReport Build(string testScriptName, DateTimeOffset startTime, DateTimeOffset endTime);
}
