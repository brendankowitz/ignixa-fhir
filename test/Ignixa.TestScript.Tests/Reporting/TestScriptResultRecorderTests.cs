using Ignixa.TestScript.Reporting;

namespace Ignixa.TestScript.Tests.Reporting;

public class TestScriptResultRecorderTests
{
    [Fact]
    public void GivenDoubleBeginPhase_WhenBeginPhaseCalledAgain_ThenThrows()
    {
        var recorder = new TestScriptResultRecorder();
        recorder.BeginPhase(TestPhaseType.Setup);
        recorder.RecordOperationResult("op", "desc", new OperationOutcome(true, 200));

        Should.Throw<InvalidOperationException>(
            () => recorder.BeginPhase(TestPhaseType.Test, "test"));
    }

    [Fact]
    public void GivenBuildCalled_WhenBeginPhaseCalled_ThenThrows()
    {
        var recorder = new TestScriptResultRecorder();
        recorder.Build("name", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        Should.Throw<InvalidOperationException>(
            () => recorder.BeginPhase(TestPhaseType.Setup));
    }

    [Fact]
    public void GivenBuildCalled_WhenRecordingOperation_ThenThrows()
    {
        var recorder = new TestScriptResultRecorder();
        recorder.Build("name", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        Should.Throw<InvalidOperationException>(
            () => recorder.RecordOperationResult("op", "desc", new OperationOutcome(true, 200)));
    }

    [Fact]
    public void GivenWarningOnlyFailedAssertion_WhenRecording_ThenOutcomeIsWarning()
    {
        var recorder = new TestScriptResultRecorder();
        recorder.BeginPhase(TestPhaseType.Test, "Test");
        recorder.RecordAssertionResult("a", "desc", new AssertionOutcome(false, WarningOnly: true, Message: "boom"));
        recorder.EndPhase();

        var report = recorder.Build("name", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        report.TestResults[0].Outcome.ShouldBe(TestScriptOutcome.Warning);
        report.TestResults[0].Actions[0].Outcome.ShouldBe(TestScriptOutcome.Warning);
    }

    [Fact]
    public void GivenSetupPhaseRecorded_ThenSetupOutcomeAvailable()
    {
        var recorder = new TestScriptResultRecorder();
        recorder.BeginPhase(TestPhaseType.Setup);
        recorder.RecordOperationResult("op", "desc", new OperationOutcome(true, 200));
        recorder.EndPhase();

        recorder.SetupOutcome.ShouldBe(TestScriptOutcome.Pass);
    }

    [Fact]
    public void GivenSetupPhaseWithError_ThenSetupOutcomeIsError()
    {
        var recorder = new TestScriptResultRecorder();
        recorder.BeginPhase(TestPhaseType.Setup);
        recorder.RecordOperationResult("op", "desc", new OperationOutcome(false, ErrorMessage: "boom"));
        recorder.EndPhase();

        recorder.SetupOutcome.ShouldBe(TestScriptOutcome.Error);
    }
}
