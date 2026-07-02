using System.Collections.Immutable;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Reporting;
using Shouldly;

namespace Ignixa.Api.E2ETests.Conformance;

public class ConformanceReportMapperTests
{
    private static TestScriptReport MakeReport(
        TestPhaseResult? setup = null,
        TestPhaseResult? teardown = null,
        IReadOnlyList<TestCaseResult>? tests = null) =>
        new()
        {
            TestScriptName = "Patient CRUD",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow,
            SetupResult = setup,
            TestResults = tests ?? [],
            TeardownResult = teardown
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

    [Fact]
    public void GivenOperationExchange_WhenMappingReport_ThenIncludesRedactedStepTrace()
    {
        // Arrange
        var request = new TestRequest
        {
            Method = HttpMethod.Get,
            Url = "Patient?identifier=abc",
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer secret",
                ["Accept"] = "application/fhir+json"
            }.ToImmutableDictionary(),
        };
        var response = new TestResponse
        {
            StatusCode = 200,
            RawBody = """{"resourceType":"Bundle","type":"searchset"}""",
            Headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/fhir+json"
            }.ToImmutableDictionary(),
        };
        var action = new ActionResult(
            "search",
            "Search patients",
            TestScriptOutcome.Pass,
            Duration: TimeSpan.FromMilliseconds(42),
            Kind: TestActionKind.Operation,
            Exchange: new HttpExchange(request, response));
        var report = MakeReport(tests: [new TestCaseResult("search patient", null, [action], TestScriptOutcome.Pass)]);

        // Act
        var results = ConformanceReportMapper.Map(report, "Search/patient.json");

        // Assert
        results.ShouldHaveSingleItem();
        results[0].Steps.ShouldHaveSingleItem();
        var step = results[0].Steps[0];
        step.Kind.ShouldBe("operation");
        step.Label.ShouldBe("search");
        step.DurationMs.ShouldBe(42);
        step.Request.ShouldNotBeNull();
        step.Request!.Method.ShouldBe("GET");
        step.Request.Url.ShouldBe("Patient?identifier=abc");
        step.Request.Headers["Authorization"].ShouldBe("[redacted]");
        step.Response.ShouldNotBeNull();
        step.Response!.StatusCode.ShouldBe(200);
        step.Response.Body.ShouldBe("""{"resourceType":"Bundle","type":"searchset"}""");
    }

    [Fact]
    public void GivenSensitiveHeadersAndUrl_WhenMappingReport_ThenRedactsSecrets()
    {
        // Arrange
        var request = new TestRequest
        {
            Method = HttpMethod.Get,
            Url = "Patient?access_token=abc123&_count=10&api_key=xyz",
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer secret",
                ["Cookie"] = "session=secret",
                ["X-Api-Key"] = "secret-key",
                ["Authentication"] = "secret-auth",
                ["X-Auth-Token"] = "secret-token",
                ["Accept"] = "application/fhir+json"
            }.ToImmutableDictionary(),
        };
        var response = new TestResponse
        {
            StatusCode = 200,
            RawBody = """{"resourceType":"Bundle","type":"searchset"}""",
            Headers = new Dictionary<string, string>
            {
                ["Set-Cookie"] = "session=secret",
                ["Content-Type"] = "application/fhir+json"
            }.ToImmutableDictionary(),
        };
        var action = new ActionResult(
            "search",
            "Search patients",
            TestScriptOutcome.Pass,
            Duration: TimeSpan.FromMilliseconds(42),
            Kind: TestActionKind.Operation,
            Exchange: new HttpExchange(request, response));
        var report = MakeReport(tests: [new TestCaseResult("search patient", null, [action], TestScriptOutcome.Pass)]);

        // Act
        var results = ConformanceReportMapper.Map(report, "Search/patient.json");

        // Assert
        results.ShouldHaveSingleItem();
        var step = results[0].Steps.ShouldHaveSingleItem();
        step.Request.ShouldNotBeNull();
        step.Request!.Headers["Authorization"].ShouldBe("[redacted]");
        step.Request.Headers["Cookie"].ShouldBe("[redacted]");
        step.Request.Headers["X-Api-Key"].ShouldBe("[redacted]");
        step.Request.Headers["Authentication"].ShouldBe("[redacted]");
        step.Request.Headers["X-Auth-Token"].ShouldBe("[redacted]");
        step.Request.Headers["Accept"].ShouldBe("application/fhir+json");
        step.Response.ShouldNotBeNull();
        step.Response!.Headers["Set-Cookie"].ShouldBe("[redacted]");
        step.Response.Headers["Content-Type"].ShouldBe("application/fhir+json");
        step.Request.Url.ShouldBe("Patient?access_token=[redacted]&_count=10&api_key=[redacted]");
    }

    [Fact]
    public void GivenSuccessfulSetupAndTeardown_WhenMappingReport_ThenIncludesAllPhaseSteps()
    {
        // Arrange
        var setup = new TestPhaseResult(
            [new ActionResult("setup-create", "Create fixture", TestScriptOutcome.Pass, Kind: TestActionKind.Operation)],
            TestScriptOutcome.Pass);
        var teardown = new TestPhaseResult(
            [new ActionResult("teardown-delete", "Delete fixture", TestScriptOutcome.Pass, Kind: TestActionKind.Operation)],
            TestScriptOutcome.Pass);
        var report = MakeReport(
            setup: setup,
            teardown: teardown,
            tests: [MakeTest("read patient", TestScriptOutcome.Pass)]);

        // Act
        var results = ConformanceReportMapper.Map(report, "CRUD/basic.json");

        // Assert
        results.ShouldHaveSingleItem();
        results[0].Steps.Select(step => step.Label).ShouldBe(["setup-create", "assert", "teardown-delete"]);
        results[0].Steps.Select(step => step.Phase).ShouldBe(["setup", "test", "teardown"]);
    }
}
