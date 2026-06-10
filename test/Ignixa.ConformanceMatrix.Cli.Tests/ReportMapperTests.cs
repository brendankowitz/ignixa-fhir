using Shouldly;
using Ignixa.ConformanceMatrix.Cli.Reporting;
using Ignixa.TestScript.Reporting;

namespace Ignixa.ConformanceMatrix.Cli.Tests;

public class ReportMapperTests
{
    private static TestScriptReport MakeReport(
        string name = "my-script",
        TestPhaseResult? setup = null,
        IReadOnlyList<TestCaseResult>? tests = null) =>
        new()
        {
            TestScriptName = name,
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow,
            SetupResult = setup,
            TestResults = tests ?? []
        };

    private static TestCaseResult MakeTest(string name, TestScriptOutcome outcome, string? message = null) =>
        new(name, null,
            [new ActionResult("assert", "assertion", outcome, message, TimeSpan.FromMilliseconds(10))],
            outcome);

    [Fact]
    public void GivenPassingTest_WhenMapped_ThenStatusIsPass()
    {
        // Arrange
        var report = MakeReport(tests: [MakeTest("tc1", TestScriptOutcome.Pass)]);

        // Act
        var results = ReportMapper.Map(report, "suite/test.json");

        // Assert
        results.ShouldHaveSingleItem();
        results[0].Status.ShouldBe("pass");
        results[0].Error.ShouldBeNull();
    }

    [Fact]
    public void GivenFailingTest_WhenMapped_ThenStatusIsFail()
    {
        // Arrange
        var report = MakeReport(tests: [MakeTest("tc1", TestScriptOutcome.Fail, "expected 200 but got 404")]);

        // Act
        var results = ReportMapper.Map(report, "suite/test.json");

        // Assert
        results.ShouldHaveSingleItem();
        results[0].Status.ShouldBe("fail");
        results[0].Error.ShouldNotBeNull();
        results[0].Error!.Received.ShouldBe("expected 200 but got 404");
    }

    [Fact]
    public void GivenErrorOutcomeTest_WhenMapped_ThenStatusIsFail()
    {
        // Arrange
        var report = MakeReport(tests: [MakeTest("tc1", TestScriptOutcome.Error, "evaluator threw")]);

        // Act
        var results = ReportMapper.Map(report, "suite/test.json");

        // Assert
        results.ShouldHaveSingleItem();
        results[0].Status.ShouldBe("fail");
    }

    [Fact]
    public void GivenWarningOutcomeTest_WhenMapped_ThenStatusIsPass()
    {
        // Arrange
        var report = MakeReport(tests: [MakeTest("tc1", TestScriptOutcome.Warning)]);

        // Act
        var results = ReportMapper.Map(report, "suite/test.json");

        // Assert
        results.ShouldHaveSingleItem();
        results[0].Status.ShouldBe("pass");
    }

    [Fact]
    public void GivenSkippedTest_WhenMapped_ThenStatusIsSkipped()
    {
        // Arrange
        var report = MakeReport(tests: [MakeTest("tc1", TestScriptOutcome.Skip)]);

        // Act
        var results = ReportMapper.Map(report, "suite/test.json");

        // Assert
        results.ShouldHaveSingleItem();
        results[0].Status.ShouldBe("skipped");
    }

    [Fact]
    public void GivenZeroTestResults_WhenMapped_ThenSyntheticSkippedIsReturned()
    {
        // Arrange
        var report = MakeReport(tests: []);

        // Act
        var results = ReportMapper.Map(report, "suite/test.json");

        // Assert
        results.ShouldHaveSingleItem();
        results[0].Status.ShouldBe("skipped");
        results[0].Error.ShouldNotBeNull();
        results[0].Error!.Assertion.ShouldBe("No tests");
    }

    [Fact]
    public void GivenFailingSetupWithNoTests_WhenMapped_ThenSingleFailResultReturned()
    {
        // Arrange
        var setup = new TestPhaseResult(
            [new ActionResult("setup-op", "setup assert", TestScriptOutcome.Fail, "server unavailable")],
            TestScriptOutcome.Fail);
        var report = MakeReport(setup: setup, tests: []);

        // Act
        var results = ReportMapper.Map(report, "suite/test.json");

        // Assert
        results.ShouldHaveSingleItem();
        results[0].Status.ShouldBe("fail");
        results[0].Id.ShouldBe("my-script");
    }

    [Fact]
    public void GivenFailingSetupWithMultipleTests_WhenMapped_ThenAllTestsFanOutAsFail()
    {
        // Arrange
        var setup = new TestPhaseResult(
            [new ActionResult("setup-op", "setup assert", TestScriptOutcome.Fail, "setup fail")],
            TestScriptOutcome.Fail);
        var tests = new List<TestCaseResult>
        {
            MakeTest("tc1", TestScriptOutcome.Pass),
            MakeTest("tc2", TestScriptOutcome.Pass)
        };
        var report = MakeReport(setup: setup, tests: tests);

        // Act
        var results = ReportMapper.Map(report, "suite/test.json");

        // Assert
        results.Count.ShouldBe(2);
        results.ShouldAllBe(r => r.Status == "fail");
        results[0].Error.ShouldNotBeNull();
        results[0].Error!.Received.ShouldBe("setup fail");
    }

    [Fact]
    public void GivenSingleTest_WhenMapped_ThenIdContainsScriptAndTestName()
    {
        // Arrange
        var report = MakeReport(
            name: "Patient CRUD",
            tests: [MakeTest("create patient", TestScriptOutcome.Pass)]);

        // Act
        var results = ReportMapper.Map(report, "crud/patient.json");

        // Assert
        results[0].Id.ShouldBe("Patient CRUD > create patient");
        results[0].File.ShouldBe("crud/patient.json");
    }

    [Theory]
    [InlineData(TestScriptOutcome.Pass, "pass")]
    [InlineData(TestScriptOutcome.Warning, "pass")]
    [InlineData(TestScriptOutcome.Fail, "fail")]
    [InlineData(TestScriptOutcome.Error, "fail")]
    [InlineData(TestScriptOutcome.Skip, "skipped")]
    public void GivenOutcome_WhenMappingStatus_ThenCorrectStringReturned(TestScriptOutcome outcome, string expected)
    {
        // Act
        var status = ReportMapper.MapStatus(outcome);

        // Assert
        status.ShouldBe(expected);
    }
}
