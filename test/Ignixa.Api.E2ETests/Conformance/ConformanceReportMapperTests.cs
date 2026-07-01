using Ignixa.TestScript.Reporting;
using Shouldly;

namespace Ignixa.Api.E2ETests.Conformance;

public class ConformanceReportMapperTests
{
    private static TestScriptReport MakeReport(
        TestPhaseResult? setup = null,
        IReadOnlyList<TestCaseResult>? tests = null) =>
        new()
        {
            TestScriptName = "Patient CRUD",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow,
            SetupResult = setup,
            TestResults = tests ?? []
        };

    private static TestCaseResult MakeTest(string name, TestScriptOutcome outcome, string? message = null) =>
        new(
            name,
            null,
            [new ActionResult("assert", "assertion", outcome, message, TimeSpan.FromMilliseconds(10))],
            outcome);

    [Fact]
    public void GivenEvaluatorErrorOutcome_WhenMappingReport_ThenMarksResultAsError()
    {
        // Arrange
        var report = MakeReport(tests: [MakeTest("create patient", TestScriptOutcome.Error, "expression failed")]);

        // Act
        var results = ConformanceReportMapper.Map(report, "CRUD/patient.json");

        // Assert
        results.ShouldHaveSingleItem();
        results[0].Status.ShouldBe("error");
        results[0].Error.ShouldNotBeNull();
        results[0].Error!.Received.ShouldBe("expression failed");
    }

    [Fact]
    public void GivenSetupErrorOutcome_WhenMappingReport_ThenMarksFanOutResultsAsError()
    {
        // Arrange
        var setup = new TestPhaseResult(
            [new ActionResult("setup", "setup failed", TestScriptOutcome.Error, "fixture missing")],
            TestScriptOutcome.Error);
        var report = MakeReport(setup: setup, tests: [MakeTest("create patient", TestScriptOutcome.Pass)]);

        // Act
        var results = ConformanceReportMapper.Map(report, "CRUD/patient.json");

        // Assert
        results.ShouldHaveSingleItem();
        results[0].Status.ShouldBe("error");
        results[0].Error.ShouldNotBeNull();
        results[0].Error!.Received.ShouldBe("fixture missing");
    }

    [Fact]
    public void GivenSetupFailOutcome_WhenMappingReport_ThenMarksFanOutResultsAsFail()
    {
        // Arrange
        var setup = new TestPhaseResult(
            [new ActionResult("setup", "setup failed", TestScriptOutcome.Fail, "expected response mismatch")],
            TestScriptOutcome.Fail);
        var report = MakeReport(setup: setup, tests: [MakeTest("create patient", TestScriptOutcome.Pass)]);

        // Act
        var results = ConformanceReportMapper.Map(report, "CRUD/patient.json");

        // Assert
        results.ShouldHaveSingleItem();
        results[0].Status.ShouldBe("fail");
        results[0].Error.ShouldNotBeNull();
        results[0].Error!.Received.ShouldBe("expected response mismatch");
    }
}
