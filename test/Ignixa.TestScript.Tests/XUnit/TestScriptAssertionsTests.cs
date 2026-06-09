using Ignixa.TestScript.Reporting;
using Ignixa.TestScript.XUnit;

namespace Ignixa.TestScript.Tests.XUnit;

public class TestScriptAssertionsTests
{
    private static readonly IReadOnlyList<ActionResult> NoActions = [];

    private static TestPhaseResult MakePhase(TestScriptOutcome outcome) =>
        new(NoActions, outcome);

    private static TestCaseResult MakeTest(int index, TestScriptOutcome outcome) =>
        new($"Test {index + 1}", null, NoActions, outcome);

    private static TestScriptReport BuildReport(
        TestScriptOutcome? setupOutcome = null,
        TestScriptOutcome? teardownOutcome = null,
        params TestScriptOutcome[] testOutcomes)
    {
        var now = DateTimeOffset.UtcNow;
        return new TestScriptReport
        {
            TestScriptName = "test-report",
            StartTime = now,
            EndTime = now,
            SetupResult = setupOutcome is { } so ? MakePhase(so) : null,
            TeardownResult = teardownOutcome is { } to ? MakePhase(to) : null,
            TestResults = testOutcomes.Select((outcome, i) => MakeTest(i, outcome)).ToList()
        };
    }

    [Fact]
    public void GivenPassingReport_WhenShouldPass_ThenNoExceptionThrown()
    {
        var report = BuildReport(testOutcomes: TestScriptOutcome.Pass);

        Should.NotThrow(() => report.ShouldPass());
    }

    [Fact]
    public void GivenFailingReport_WhenShouldPass_ThenThrowsShouldlyException()
    {
        var report = BuildReport(testOutcomes: TestScriptOutcome.Fail);

        Should.Throw<ShouldAssertException>(() => report.ShouldPass())
            .Message.ShouldContain("test-report");
    }

    [Fact]
    public void GivenFailingReport_WhenShouldFail_ThenNoExceptionThrown()
    {
        var report = BuildReport(testOutcomes: TestScriptOutcome.Fail);

        Should.NotThrow(() => report.ShouldFail());
    }

    [Fact]
    public void GivenPassingReport_WhenShouldFail_ThenThrowsShouldlyException()
    {
        var report = BuildReport(testOutcomes: TestScriptOutcome.Pass);

        Should.Throw<ShouldAssertException>(() => report.ShouldFail())
            .Message.ShouldContain("test-report");
    }

    [Fact]
    public void GivenReportWithThreeTests_WhenShouldHaveTestCount3_ThenNoExceptionThrown()
    {
        var report = BuildReport(testOutcomes: [
            TestScriptOutcome.Pass, TestScriptOutcome.Pass, TestScriptOutcome.Pass]);

        Should.NotThrow(() => report.ShouldHaveTestCount(3));
    }

    [Fact]
    public void GivenReportWithOneTest_WhenShouldHaveTestCount3_ThenThrowsShouldlyException()
    {
        var report = BuildReport(testOutcomes: TestScriptOutcome.Pass);

        Should.Throw<ShouldAssertException>(() => report.ShouldHaveTestCount(3))
            .Message.ShouldContain("test-report");
    }

    [Fact]
    public void GivenReportWithPassingSetup_WhenShouldHavePassingSetup_ThenNoExceptionThrown()
    {
        var report = BuildReport(setupOutcome: TestScriptOutcome.Pass);

        Should.NotThrow(() => report.ShouldHavePassingSetup());
    }

    [Fact]
    public void GivenReportWithFailingSetup_WhenShouldHavePassingSetup_ThenThrowsShouldlyException()
    {
        var report = BuildReport(setupOutcome: TestScriptOutcome.Fail);

        Should.Throw<ShouldAssertException>(() => report.ShouldHavePassingSetup())
            .Message.ShouldContain("test-report");
    }

    [Fact]
    public void GivenReportWithNoSetupResult_WhenShouldHavePassingSetup_ThenThrowsShouldlyException()
    {
        var report = BuildReport(testOutcomes: TestScriptOutcome.Pass);

        Should.Throw<ShouldAssertException>(() => report.ShouldHavePassingSetup())
            .Message.ShouldContain("no setup result");
    }

    [Fact]
    public void GivenReportWithPassingTeardown_WhenShouldHavePassingTeardown_ThenNoExceptionThrown()
    {
        var report = BuildReport(teardownOutcome: TestScriptOutcome.Pass);

        Should.NotThrow(() => report.ShouldHavePassingTeardown());
    }

    [Fact]
    public void GivenReportWithFailingTeardown_WhenShouldHavePassingTeardown_ThenThrowsShouldlyException()
    {
        var report = BuildReport(teardownOutcome: TestScriptOutcome.Fail);

        Should.Throw<ShouldAssertException>(() => report.ShouldHavePassingTeardown())
            .Message.ShouldContain("test-report");
    }

    [Fact]
    public void GivenReportWithNoTeardownResult_WhenShouldHavePassingTeardown_ThenThrowsShouldlyException()
    {
        var report = BuildReport(testOutcomes: TestScriptOutcome.Pass);

        Should.Throw<ShouldAssertException>(() => report.ShouldHavePassingTeardown())
            .Message.ShouldContain("no teardown result");
    }
}
