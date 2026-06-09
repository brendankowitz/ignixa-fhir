using System.Text.Json.Nodes;
using Ignixa.TestScript.Reporting;

namespace Ignixa.TestScript.Tests.Reporting;

public class TestReportResourceGeneratorTests
{
    [Fact]
    public void GivenPassingReport_WhenGenerating_ThenProducesValidTestReport()
    {
        var report = new TestScriptReport
        {
            TestScriptName = "ReadPatientTest",
            StartTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 1, 1, 0, 0, 1, TimeSpan.Zero),
            TestResults =
            [
                new TestCaseResult("ReadPatient", "Read a patient", [
                    new ActionResult("read", "Read Patient", TestScriptOutcome.Pass),
                    new ActionResult("assert-status", "Check 200", TestScriptOutcome.Pass)
                ], TestScriptOutcome.Pass)
            ]
        };

        var json = TestReportResourceGenerator.Generate(report);

        json.ShouldNotBeNull();
        json["resourceType"]?.GetValue<string>().ShouldBe("TestReport");
        json["result"]?.GetValue<string>().ShouldBe("pass");
        json["name"]?.GetValue<string>().ShouldBe("ReadPatientTest");
        json["test"]?.AsArray().Count.ShouldBe(1);
    }

    [Fact]
    public void GivenFailingReport_WhenGenerating_ThenResultIsFail()
    {
        var report = new TestScriptReport
        {
            TestScriptName = "FailTest",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow,
            TestResults =
            [
                new TestCaseResult("FailingTest", null, [
                    new ActionResult(null, null, TestScriptOutcome.Fail, "Expected 200 got 404")
                ], TestScriptOutcome.Fail)
            ]
        };

        var json = TestReportResourceGenerator.Generate(report);

        json["result"]?.GetValue<string>().ShouldBe("fail");
    }

    [Fact]
    public void GivenWarningAction_WhenGenerating_ThenActionResultIsWarning()
    {
        var report = new TestScriptReport
        {
            TestScriptName = "WarnTest",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow,
            TestResults =
            [
                new TestCaseResult("WarnCase", null, [
                    new ActionResult("a", null, TestScriptOutcome.Warning, "soft fail")
                ], TestScriptOutcome.Warning)
            ]
        };

        var json = TestReportResourceGenerator.Generate(report);

        var actionResult = json["test"]!.AsArray()[0]!["action"]!.AsArray()[0]!["result"]!.GetValue<string>();
        actionResult.ShouldBe("warning");
    }

    [Fact]
    public void GivenWarningOverall_WhenGenerating_ThenReportResultIsPass()
    {
        var report = new TestScriptReport
        {
            TestScriptName = "WarnReport",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow,
            TeardownResult = new TestPhaseResult([], TestScriptOutcome.Error)
        };

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Warning);

        var json = TestReportResourceGenerator.Generate(report);

        json["result"]?.GetValue<string>().ShouldBe("pass");
    }

    [Fact]
    public void GivenErrorReport_WhenGenerating_ThenReportResultIsFailNotError()
    {
        var report = new TestScriptReport
        {
            TestScriptName = "ErrorReport",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow,
            TestResults =
            [
                new TestCaseResult("Boom", null, [
                    new ActionResult(null, null, TestScriptOutcome.Error, "engine bug")
                ], TestScriptOutcome.Error)
            ]
        };

        var json = TestReportResourceGenerator.Generate(report);

        json["result"]?.GetValue<string>().ShouldBe("fail");
        var actionResult = json["test"]!.AsArray()[0]!["action"]!.AsArray()[0]!["result"]!.GetValue<string>();
        actionResult.ShouldBe("error");
    }

    [Fact]
    public void GivenSkippedTestAction_WhenGenerating_ThenActionResultIsSkip()
    {
        var report = new TestScriptReport
        {
            TestScriptName = "SkippedReport",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow,
            SetupResult = new TestPhaseResult([], TestScriptOutcome.Pass),
            TestResults =
            [
                new TestCaseResult("Skipped", null, [
                    new ActionResult(null, null, TestScriptOutcome.Skip, "version mismatch")
                ], TestScriptOutcome.Skip)
            ]
        };

        var json = TestReportResourceGenerator.Generate(report);

        var actionResult = json["test"]!.AsArray()[0]!["action"]!.AsArray()[0]!["result"]!.GetValue<string>();
        actionResult.ShouldBe("skip");
    }
}
