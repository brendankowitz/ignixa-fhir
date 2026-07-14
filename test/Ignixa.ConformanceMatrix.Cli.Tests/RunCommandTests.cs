using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Shouldly;
using Ignixa.ConformanceMatrix.Cli.Commands;
using Ignixa.ConformanceMatrix.Cli.Reporting;

namespace Ignixa.ConformanceMatrix.Cli.Tests;

public class RunCommandTests
{
    private static ImplReportResult MakeResult(string status) =>
        new()
        {
            Id = "test",
            File = "test.json",
            Status = status,
            DurationMs = 0
        };

    [Fact]
    public void GivenMixedResults_WhenFormattingSummary_ThenIncludesSkippedAndErrorCounts()
    {
        // Arrange
        var results = new List<ImplReportResult>
        {
            MakeResult("pass"),
            MakeResult("fail"),
            MakeResult("skipped"),
            MakeResult("error"),
            MakeResult("skipped")
        };

        // Act
        var summary = RunCommand.FormatOutcomeSummary(results);

        // Assert
        summary.ShouldBe("1 passed, 1 failed, 2 skipped, 1 error(s)");
    }

    [Fact]
    public void GivenBareTokenValue_WhenNormalizingAuthHeader_ThenUsesAuthorizationHeader()
    {
        var (name, value) = RunCommand.ParseAuthHeader("Bearer abc123");

        name.ShouldBe("Authorization");
        value.ShouldBe("Bearer abc123");
    }

    [Fact]
    public void GivenExplicitHeaderValue_WhenNormalizingAuthHeader_ThenPreservesHeaderName()
    {
        var (name, value) = RunCommand.ParseAuthHeader("X-Test: value");

        name.ShouldBe("X-Test");
        value.ShouldBe("value");
    }

    private static JsonObject MakeTestReport(string display) =>
        new()
        {
            ["resourceType"] = "TestReport",
            ["testScript"] = new JsonObject { ["display"] = display }
        };

    [Fact]
    public void GivenCustomAuthScheme_WhenNormalizingAuthHeader_ThenTreatsItAsBareCredential()
    {
        // Arrange: a scheme not on any hardcoded list, whose credential contains a colon.
        var (name, value) = RunCommand.ParseAuthHeader("AWS4-HMAC-SHA256 Credential=abc/20260714:xyz");

        name.ShouldBe("Authorization");
        value.ShouldBe("AWS4-HMAC-SHA256 Credential=abc/20260714:xyz");
    }

    [Fact]
    public void GivenNoAuthHeader_WhenApplying_ThenSucceedsWithoutSettingAHeader()
    {
        // Arrange
        using var httpClient = new HttpClient();

        // Act
        var error = RunCommand.ApplyAuthHeader(httpClient, null);

        // Assert
        error.ShouldBeNull();
        httpClient.DefaultRequestHeaders.Authorization.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Authorization:")]
    [InlineData("X-Api-Key:   ")]
    public void GivenAuthHeaderWithNoValue_WhenApplying_ThenReportsErrorRatherThanRunningUnauthenticated(string authHeader)
    {
        // Arrange: an env var expanding to empty is the common case; applying nothing would run the
        // whole suite unauthenticated and report every 401 as a legitimate failure.
        using var httpClient = new HttpClient();

        // Act
        var error = RunCommand.ApplyAuthHeader(httpClient, authHeader);

        // Assert
        error.ShouldNotBeNull();
        error.ShouldContain("resolves to no header value");
        httpClient.DefaultRequestHeaders.Authorization.ShouldBeNull();
    }

    [Fact]
    public void GivenInvalidHeaderName_WhenApplying_ThenReportsErrorRatherThanDroppingTheCredential()
    {
        // Arrange: '@' is not a valid HTTP token, so TryAddWithoutValidation returns false.
        using var httpClient = new HttpClient();

        // Act
        var error = RunCommand.ApplyAuthHeader(httpClient, "Api@Key: abc123");

        // Assert
        error.ShouldNotBeNull();
        error.ShouldContain("not a valid HTTP header name");
    }

    [Fact]
    public void GivenBearerToken_WhenApplying_ThenSetsAuthorizationHeaderOnTheClient()
    {
        // Arrange
        using var httpClient = new HttpClient();

        // Act
        var error = RunCommand.ApplyAuthHeader(httpClient, "Bearer abc123");

        // Assert
        error.ShouldBeNull();
        httpClient.DefaultRequestHeaders.Authorization!.Scheme.ShouldBe("Bearer");
        httpClient.DefaultRequestHeaders.Authorization.Parameter.ShouldBe("abc123");
    }

    [Fact]
    public void GivenCustomHeaderName_WhenApplying_ThenAddsItToTheClient()
    {
        // Arrange
        using var httpClient = new HttpClient();

        // Act
        var error = RunCommand.ApplyAuthHeader(httpClient, "X-Api-Key: abc123");

        // Assert
        error.ShouldBeNull();
        httpClient.DefaultRequestHeaders.GetValues("X-Api-Key").ShouldBe(["abc123"]);
    }

    [Fact]
    public void GivenUnparseableAuthorizationValue_WhenApplying_ThenFallsBackToAddingItVerbatim()
    {
        // Arrange: a credential Authorization cannot parse must still reach the wire.
        using var httpClient = new HttpClient();

        // Act
        var error = RunCommand.ApplyAuthHeader(httpClient, "Authorization: AWS4-HMAC-SHA256 Credential=abc/20260714:xyz");

        // Assert
        error.ShouldBeNull();
        httpClient.DefaultRequestHeaders.GetValues("Authorization")
            .ShouldBe(["AWS4-HMAC-SHA256 Credential=abc/20260714:xyz"]);
    }

    [Fact]
    public void GivenJsonFormat_WhenBuildingPayload_ThenProducesTheNativeImplReportShape()
    {
        // Arrange: this is the shape 'merge' deserializes; a regression breaks the matrix pipeline.
        var results = new List<ImplReportResult> { MakeResult("pass") };

        // Act
        var payload = RunCommand.BuildPayload(ReportFormat.Json, "my-server", DateTimeOffset.UnixEpoch, 42, results, []);

        // Assert
        var parsed = JsonNode.Parse(payload)!;
        parsed["impl"]!.GetValue<string>().ShouldBe("my-server");
        parsed["duration_ms"]!.GetValue<long>().ShouldBe(42);
        parsed["results"]!.AsArray().Count.ShouldBe(1);
    }

    [Fact]
    public void GivenFhirFormat_WhenBuildingPayload_ThenProducesATestReportBundle()
    {
        // Act
        var payload = RunCommand.BuildPayload(
            ReportFormat.Fhir, "my-server", DateTimeOffset.UnixEpoch, 42, [], [MakeTestReport("Search/basic.json")]);

        // Assert
        var parsed = JsonNode.Parse(payload)!;
        parsed["resourceType"]!.GetValue<string>().ShouldBe("Bundle");
        parsed["entry"]!.AsArray().Count.ShouldBe(1);
    }

    [Fact]
    public void GivenNoReports_WhenBuildingPayload_ThenOmitsEntryBecauseFhirForbidsEmptyArrays()
    {
        // Arrange: reachable when every script fails to parse.

        // Act
        var payload = RunCommand.BuildFhirBundle([], DateTimeOffset.UnixEpoch);

        // Assert
        payload["resourceType"]!.GetValue<string>().ShouldBe("Bundle");
        payload.ContainsKey("entry").ShouldBeFalse();
    }

    [Fact]
    public void GivenSingleReport_WhenBuildingPayload_ThenStillReturnsBundleCollection()
    {
        var payload = RunCommand.BuildFhirBundle([MakeTestReport("Search/basic.json")], DateTimeOffset.UnixEpoch);

        payload.ShouldNotBeNull();
        payload["resourceType"]!.GetValue<string>().ShouldBe("Bundle");
        payload["type"]!.GetValue<string>().ShouldBe("collection");
        payload["entry"]!.AsArray().Count.ShouldBe(1);
    }

    [Fact]
    public void GivenMultipleReports_WhenBuildingPayload_ThenEntriesCarryUniqueAbsoluteFullUrls()
    {
        // Arrange/Act: fullUrl must be absolute per R4, and unique within the Bundle per bdl-7.
        var payload = RunCommand.BuildFhirBundle(
            [MakeTestReport("Search/intervals.json"), MakeTestReport("CRUD/basic.json")],
            DateTimeOffset.UnixEpoch);

        // Assert
        var entries = payload["entry"]!.AsArray();
        entries.Count.ShouldBe(2);

        var fullUrls = entries.Select(e => e!["fullUrl"]!.GetValue<string>()).ToList();
        fullUrls.ShouldAllBe(url => Uri.IsWellFormedUriString(url, UriKind.Absolute));
        fullUrls.Distinct().Count().ShouldBe(2);
        entries[0]!["resource"]!["resourceType"]!.GetValue<string>().ShouldBe("TestReport");
    }

    [Fact]
    public void GivenReports_WhenBuildingPayload_ThenBundleCarriesRunTimestamp()
    {
        var startedAt = new DateTimeOffset(2026, 7, 14, 9, 30, 0, TimeSpan.Zero);

        var payload = RunCommand.BuildFhirBundle([MakeTestReport("Search/basic.json")], startedAt);

        payload["timestamp"]!.GetValue<string>().ShouldBe(startedAt.ToString("o"));
    }

    [Fact]
    public void GivenParseFailure_WhenBuildingOutcome_ThenReportsStructureWithTheOffendingFile()
    {
        // Arrange/Act: 'structure' is FHIR's "unable to parse the content completely, invalid syntax".
        var outcome = RunCommand.BuildParseErrorOutcome("Search/broken.json", "Required field 'name' is missing");

        // Assert
        outcome["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
        var issue = outcome["issue"]!.AsArray().ShouldHaveSingleItem()!;
        issue["severity"]!.GetValue<string>().ShouldBe("error");
        issue["code"]!.GetValue<string>().ShouldBe("structure");
        issue["diagnostics"]!.GetValue<string>()
            .ShouldBe("Search/broken.json: Required field 'name' is missing");
    }

    [Fact]
    public void GivenEvaluatorException_WhenBuildingOutcome_ThenReportsExceptionWithTheOffendingFile()
    {
        // Arrange: the message alone ("Object reference not set...") does not identify the failure,
        // so the type is carried too.
        var error = new InvalidOperationException("fixture 'obs-past' was never resolved");

        // Act: 'exception' is FHIR's "An unexpected internal error has occurred".
        var outcome = RunCommand.BuildEvaluatorErrorOutcome("Search/intervals.json", error);

        // Assert
        outcome["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
        var issue = outcome["issue"]!.AsArray().ShouldHaveSingleItem()!;
        issue["severity"]!.GetValue<string>().ShouldBe("error");
        issue["code"]!.GetValue<string>().ShouldBe("exception");
        issue["diagnostics"]!.GetValue<string>().ShouldBe(
            "Search/intervals.json: InvalidOperationException: fixture 'obs-past' was never resolved");
    }

    [Fact]
    public void GivenTestReportsAndOperationOutcomes_WhenBuildingBundle_ThenBothCoexistAsCollectionEntries()
    {
        // Arrange: Bundle.type = collection imposes no homogeneity constraint, so a run that both
        // executed and failed to parse scripts is representable in one Bundle.
        var outcome = OperationOutcomeResourceGenerator.Generate(
            OperationOutcomeResourceGenerator.StructureIssueCode, "Search/broken.json: invalid syntax");

        // Act
        var payload = RunCommand.BuildFhirBundle(
            [MakeTestReport("Search/intervals.json"), outcome], DateTimeOffset.UnixEpoch);

        // Assert
        payload["type"]!.GetValue<string>().ShouldBe("collection");
        var entries = payload["entry"]!.AsArray();
        entries.Count.ShouldBe(2);

        var resourceTypes = entries.Select(e => e!["resource"]!["resourceType"]!.GetValue<string>()).ToList();
        resourceTypes.ShouldBe(["TestReport", "OperationOutcome"]);

        var fullUrls = entries.Select(e => e!["fullUrl"]!.GetValue<string>()).ToList();
        fullUrls.ShouldAllBe(url => url.StartsWith("urn:uuid:", StringComparison.Ordinal));
        fullUrls.Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task GivenUnparseableScript_WhenRunningAsFhir_ThenBundleRecordsItAsAnOperationOutcome()
    {
        // Arrange: without this the failure exists nowhere on disk — the Bundle would just be short
        // an entry, which reads as a clean run of fewer scripts.
        using var workspace = new TempWorkspace();
        workspace.WriteScript("broken.json", "{ this is not a TestScript");

        // Act
        var exitCode = await RunCommand.RunAsync(
            UnreachableServer, workspace.TestsPath, "my-server", workspace.OutPath,
            fhirVersion: null, authHeader: null, ReportFormat.Fhir, CancellationToken.None);

        // Assert
        exitCode.ShouldBe(1);

        var bundle = JsonNode.Parse(await File.ReadAllTextAsync(workspace.OutPath))!;
        var resources = bundle["entry"]!.AsArray().Select(e => e!["resource"]!).ToList();
        var outcome = resources.ShouldHaveSingleItem();
        outcome["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");

        var issue = outcome["issue"]!.AsArray().ShouldHaveSingleItem()!;
        issue["code"]!.GetValue<string>().ShouldBe("structure");
        issue["diagnostics"]!.GetValue<string>().ShouldContain("broken.json");
    }

    [Fact]
    public async Task GivenUnparseableScript_WhenRunningAsJson_ThenNoOperationOutcomeIsEmitted()
    {
        // Arrange: the native report already records the failure as an "error" result; merge does
        // not read FHIR resources.
        using var workspace = new TempWorkspace();
        workspace.WriteScript("broken.json", "{ this is not a TestScript");

        // Act
        var exitCode = await RunCommand.RunAsync(
            UnreachableServer, workspace.TestsPath, "my-server", workspace.OutPath,
            fhirVersion: null, authHeader: null, ReportFormat.Json, CancellationToken.None);

        // Assert
        exitCode.ShouldBe(1);

        var payload = await File.ReadAllTextAsync(workspace.OutPath);
        payload.ShouldNotContain("OperationOutcome");

        var parsed = JsonNode.Parse(payload)!;
        parsed["impl"]!.GetValue<string>().ShouldBe("my-server");
        parsed["results"]!.AsArray().ShouldHaveSingleItem()!["status"]!.GetValue<string>().ShouldBe("error");
    }

    [Fact]
    public async Task GivenMissingTestsDirectory_WhenRunning_ThenReturnsUsageExitCodeDistinctFromTestFailure()
    {
        // Arrange: CI must tell "the invocation was wrong, nothing ran" from "the suite ran and
        // tests failed" (1).
        using var workspace = new TempWorkspace();
        var missing = Path.Combine(workspace.TestsPath, "does-not-exist");

        // Act
        var exitCode = await RunCommand.RunAsync(
            UnreachableServer, missing, "my-server", workspace.OutPath,
            fhirVersion: null, authHeader: null, ReportFormat.Fhir, CancellationToken.None);

        // Assert
        exitCode.ShouldBe(2);
    }

    [Fact]
    public async Task GivenRelativeServerUri_WhenRunning_ThenReturnsUsageExitCode()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteScript("broken.json", "{");

        var exitCode = await RunCommand.RunAsync(
            "not-a-uri", workspace.TestsPath, "my-server", workspace.OutPath,
            fhirVersion: null, authHeader: null, ReportFormat.Fhir, CancellationToken.None);

        exitCode.ShouldBe(2);
    }

    [Fact]
    public async Task GivenEmptyTestsDirectory_WhenRunning_ThenReturnsUsageExitCode()
    {
        using var workspace = new TempWorkspace();

        var exitCode = await RunCommand.RunAsync(
            UnreachableServer, workspace.TestsPath, "my-server", workspace.OutPath,
            fhirVersion: null, authHeader: null, ReportFormat.Fhir, CancellationToken.None);

        exitCode.ShouldBe(2);
    }

    [Fact]
    public async Task GivenUnusableAuthHeader_WhenRunning_ThenReturnsUsageExitCodeWithoutRunningTheSuite()
    {
        // Arrange: running unauthenticated would report every 401 as a legitimate test failure.
        using var workspace = new TempWorkspace();
        workspace.WriteScript("broken.json", "{");

        // Act
        var exitCode = await RunCommand.RunAsync(
            UnreachableServer, workspace.TestsPath, "my-server", workspace.OutPath,
            fhirVersion: null, authHeader: "Authorization:", ReportFormat.Fhir, CancellationToken.None);

        // Assert
        exitCode.ShouldBe(2);
        File.Exists(workspace.OutPath).ShouldBeFalse();
    }

    [Fact]
    public void GivenWritableOutPath_WhenPreparingOutputDirectory_ThenCreatesTheDirectory()
    {
        // Arrange: validated before the suite runs, so a typo cannot discard a completed run.
        using var workspace = new TempWorkspace();
        var nested = Path.Combine(workspace.Root, "reports", "nested", "out.json");

        // Act
        var error = RunCommand.PrepareOutputDirectory(nested);

        // Assert
        error.ShouldBeNull();
        Directory.Exists(Path.GetDirectoryName(nested)!).ShouldBeTrue();
    }

    [Fact]
    public void GivenOutPathWithInvalidCharacters_WhenPreparingOutputDirectory_ThenReportsErrorRatherThanThrowing()
    {
        // Arrange: Path.GetFullPath throws on these; that is a usage error, not an internal fault.
        var error = RunCommand.PrepareOutputDirectory("\0invalid\0/report.json");

        // Assert
        error.ShouldNotBeNull();
        error.ShouldContain("--out path is unusable");
    }

    // Loopback port 1 refuses immediately, so tests that must not reach a server stay fast and
    // deterministic rather than waiting on a DNS or connect timeout.
    private const string UnreachableServer = "http://127.0.0.1:1";

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), $"ignixa-matrix-tests-{Guid.NewGuid():N}");
            TestsPath = Path.Combine(Root, "tests");
            OutPath = Path.Combine(Root, "reports", "report.json");
            Directory.CreateDirectory(TestsPath);
        }

        public string Root { get; }

        public string TestsPath { get; }

        public string OutPath { get; }

        public void WriteScript(string name, string content) =>
            File.WriteAllText(Path.Combine(TestsPath, name), content);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // A leaked temp directory must not fail an otherwise passing test.
            }
        }
    }

    [Fact]
    public async Task GivenSuccessfulMetadataResponse_WhenFetching_ThenReturnsParsedCapabilityStatement()
    {
        using var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"resourceType":"CapabilityStatement","status":"active"}""",
                    Encoding.UTF8,
                    "application/fhir+json")
            });
        using var httpClient = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://test/") };

        var result = await RunCommand.FetchCapabilityStatementAsync(httpClient, CancellationToken.None);

        result.ShouldNotBeNull();
        result.ResourceType.ShouldBe("CapabilityStatement");
    }

    [Fact]
    public async Task GivenNonSuccessMetadataResponse_WhenFetching_ThenReturnsNull()
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var httpClient = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://test/") };

        var result = await RunCommand.FetchCapabilityStatementAsync(httpClient, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GivenUnparseableMetadataBody_WhenFetching_ThenReturnsNull()
    {
        using var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not json", Encoding.UTF8, "application/fhir+json")
            });
        using var httpClient = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://test/") };

        var result = await RunCommand.FetchCapabilityStatementAsync(httpClient, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GivenNetworkFailure_WhenFetching_ThenReturnsNull()
    {
        using var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));
        using var httpClient = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://test/") };

        var result = await RunCommand.FetchCapabilityStatementAsync(httpClient, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GivenCancellationRequested_WhenFetching_ThenThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var handler = new StubHandler(_ => throw new OperationCanceledException());
        using var httpClient = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://test/") };

        await Should.ThrowAsync<OperationCanceledException>(
            () => RunCommand.FetchCapabilityStatementAsync(httpClient, cts.Token));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }
}
