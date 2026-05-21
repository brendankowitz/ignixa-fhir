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
}
