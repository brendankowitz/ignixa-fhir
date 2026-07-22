using System.Text;
using Ignixa.ConformanceMatrix.Cli.Serving;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Reporting;
using Shouldly;

namespace Ignixa.ConformanceMatrix.Cli.Tests.Serving;

public class RunResponseMapperTests
{
    [Fact]
    public void GivenSetupTestAndTeardownOperationsWithAFailingAssertion_WhenMapped_ThenOperationsAreOrderedAndCountsMatch()
    {
        // Arrange
        var setupOp = new ActionResult(
            "Setup Op", "create fixture", TestScriptOutcome.Pass, null, TimeSpan.FromMilliseconds(5), TestActionKind.Operation,
            new HttpExchange(
                new TestRequest { Method = HttpMethod.Post, Url = "Patient" },
                new TestResponse { StatusCode = 201, RawBody = """{"resourceType":"Patient"}""" }));

        var testOp = new ActionResult(
            "Search Op", "search patients", TestScriptOutcome.Pass, null, TimeSpan.FromMilliseconds(42), TestActionKind.Operation,
            new HttpExchange(
                new TestRequest { Method = HttpMethod.Get, Url = "Patient?name=Smith" },
                new TestResponse { StatusCode = 200, RawBody = "1234567890" }));

        var testAssertFail = new ActionResult(
            "Status check", "expect 200", TestScriptOutcome.Fail, "Expected response 'okay' but got status 404", TimeSpan.Zero);

        var teardownOp = new ActionResult(
            "Teardown Op", "delete fixture", TestScriptOutcome.Error, "connection reset", TimeSpan.FromMilliseconds(3), TestActionKind.Operation,
            new HttpExchange(new TestRequest { Method = HttpMethod.Delete, Url = "Patient/123" }, null));

        var report = new TestScriptReport
        {
            TestScriptName = "PatientSearch",
            StartTime = DateTimeOffset.UnixEpoch,
            EndTime = DateTimeOffset.UnixEpoch.AddMilliseconds(500),
            SetupResult = new TestPhaseResult([setupOp], TestScriptOutcome.Pass),
            TestResults = [new TestCaseResult("Search test", null, [testOp, testAssertFail], TestScriptOutcome.Fail)],
            TeardownResult = new TestPhaseResult([teardownOp], TestScriptOutcome.Error)
        };

        // Act
        var response = RunResponseMapper.Map(report, "PatientSearch");

        // Assert
        response.Passed.ShouldBeFalse();
        response.TestScriptId.ShouldBe("PatientSearch");
        response.DurationMs.ShouldBe(500);
        response.FailedAssertionCount.ShouldBe(1);

        response.Operations.Count.ShouldBe(3);

        response.Operations[0].Name.ShouldBe("Setup Op");
        response.Operations[0].Method.ShouldBe("POST");
        response.Operations[0].DurationMs.ShouldBe(5);
        response.Operations[0].ResponseBytes.ShouldBe(Encoding.UTF8.GetByteCount("""{"resourceType":"Patient"}"""));
        response.Operations[0].Passed.ShouldBeTrue();

        response.Operations[1].Name.ShouldBe("Search Op");
        response.Operations[1].StatusCode.ShouldBe(200);
        response.Operations[1].ResponseBytes.ShouldBe(10);

        response.Operations[2].Name.ShouldBe("Teardown Op");
        response.Operations[2].Method.ShouldBe("DELETE");
        response.Operations[2].StatusCode.ShouldBe(0);
        response.Operations[2].ResponseBytes.ShouldBe(0);
        response.Operations[2].Passed.ShouldBeFalse();

        response.Summary.ShouldContain("Status check");
        response.Summary.ShouldContain("Expected response 'okay' but got status 404");
    }

    [Fact]
    public void GivenAnAllPassingReport_WhenMapped_ThenPassedIsTrueAndSummaryIsPassed()
    {
        // Arrange
        var op = new ActionResult(
            "Read Op", "read patient", TestScriptOutcome.Pass, null, TimeSpan.FromMilliseconds(9), TestActionKind.Operation,
            new HttpExchange(
                new TestRequest { Method = HttpMethod.Get, Url = "Patient/1" },
                new TestResponse { StatusCode = 200, RawBody = "{}" }));
        var assertion = new ActionResult("check", "status ok", TestScriptOutcome.Pass);

        var report = new TestScriptReport
        {
            TestScriptName = "PatientRead",
            StartTime = DateTimeOffset.UnixEpoch,
            EndTime = DateTimeOffset.UnixEpoch.AddMilliseconds(9),
            TestResults = [new TestCaseResult("tc1", null, [op, assertion], TestScriptOutcome.Pass)]
        };

        // Act
        var response = RunResponseMapper.Map(report, "PatientRead");

        // Assert
        response.Passed.ShouldBeTrue();
        response.Summary.ShouldBe("Passed");
        response.FailedAssertionCount.ShouldBe(0);
        response.Operations.ShouldHaveSingleItem();
    }

    [Fact]
    public void GivenAWarningOnlyOutcome_WhenMapped_ThenPassedIsTrue()
    {
        // Arrange: teardown never fails the overall result but can surface as Warning.
        var teardownOp = new ActionResult(
            "Teardown Op", "delete fixture", TestScriptOutcome.Error, "boom", TimeSpan.FromMilliseconds(1), TestActionKind.Operation);

        var report = new TestScriptReport
        {
            TestScriptName = "PatientRead",
            StartTime = DateTimeOffset.UnixEpoch,
            EndTime = DateTimeOffset.UnixEpoch.AddMilliseconds(1),
            TestResults = [new TestCaseResult("tc1", null, [], TestScriptOutcome.Pass)],
            TeardownResult = new TestPhaseResult([teardownOp], TestScriptOutcome.Error)
        };

        // Act
        var response = RunResponseMapper.Map(report, "PatientRead");

        // Assert
        report.OverallOutcome.ShouldBe(TestScriptOutcome.Warning);
        response.Passed.ShouldBeTrue();
        response.Summary.ShouldBe("Passed");
    }
}
